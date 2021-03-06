// Copyright 2013 Zynga Inc.
//	
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//		
//      Unless required by applicable law or agreed to in writing, software
//      distributed under the License is distributed on an "AS IS" BASIS,
//      WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//      See the License for the specific language governing permissions and
//      limitations under the License.

namespace flash.display3D.textures {
	
	using System;
	using System.IO;
	using flash.utils;
	using flash.display;
	using flash.display3D;
	using flash.events;
	
#if PLATFORM_MONOMAC
	using MonoMac.OpenGL;
#elif PLATFORM_MONOTOUCH
	using OpenTK.Graphics.ES20;
#elif PLATFORM_MONODROID
	using OpenTK.Graphics.ES20;
	using TextureTarget = OpenTK.Graphics.ES20.All;
	using PixelInternalFormat = OpenTK.Graphics.ES20.All;
	using PixelFormat = OpenTK.Graphics.ES20.All;
	using PixelType = OpenTK.Graphics.ES20.All;
#endif

	public class Texture : TextureBase {
		
		//
		// Methods
		//

#if OPENGL

		public Texture(Context3D context, int width, int height, string format, 
		                        bool optimizeForRenderToTexture, int streamingLevels)
			: base(TextureTarget.Texture2D)
		{
			mContext = context;
			mWidth = width;
			mHeight = height;
			mFormat = format;
			mOptimizeForRenderToTexture = optimizeForRenderToTexture;
			mStreamingLevels = streamingLevels;

			// we do this to clear the texture on creation
			// $$TODO we dont need to allocate a bitmapdata to do this, we should just use a PBO and clear it
			if (optimizeForRenderToTexture) {
				var clearData = new BitmapData(width, height);
				uploadFromBitmapData(clearData);
				clearData.dispose();
			}
		}

		private static int sColor = 0;
		private static uint[] sColors = new uint[] {0x0000FF, 0x00FF00, 0xFF0000, 0xFF00FF, 0x00FFFF, 0xFFFF00};

		private bool mDidUpload = false;

		private static int sMemoryUsedForTextures = 0;

		enum AtfType 
		{
			NORMAL = 0,
			CUBE_MAP = 1
		}

		enum AtfFormat
		{
			RGB888 = 0,
			RGBA8888 = 1,
			Compressed = 2,
			Block = 5
		}

		private static uint readUInt24(ByteArray data)
		{
			uint value;
			value  = (data.readUnsignedByte() << 16);
			value |= (data.readUnsignedByte() << 8);
			value |=  data.readUnsignedByte();
			return value;
		}

		private unsafe void uploadATFTextureFromByteArray (ByteArray data, uint byteArrayOffset)
		{
			data.position = byteArrayOffset;

			// read atf signature
			string signature = data.readUTFBytes(3);
			if (signature != "ATF") {
				throw new InvalidDataException("ATF signature not found");
			}

			// read atf length
			uint length = readUInt24(data);
			if ((byteArrayOffset + length) > data.length) {
				throw new InvalidDataException("ATF length exceeds byte array length");
			}

			// get format
			uint tdata = data.readUnsignedByte( );
			AtfType type = (AtfType)(tdata >> 7); 	
			if (type != AtfType.NORMAL) {
				throw new NotImplementedException("ATF Cube maps are not supported");
			}

//			Removing ATF format limitation to allow for multiple format support.
//			AtfFormat format = (AtfFormat)(tdata & 0x7f);	
//			if (format != AtfFormat.Block) {
//				throw new NotImplementedException("Only ATF block compressed textures are supported");
//			}

			// get dimensions
			int width =  (1 << (int)data.readUnsignedByte());
			int height = (1 << (int)data.readUnsignedByte());

			if (width != mWidth || height != mHeight) {
				throw new InvalidDataException("ATF Width and height dont match");
			}

			// get mipmap count
			int mipCount = (int)data.readUnsignedByte();

			// read all mipmap levels
			for (int level=0; level < mipCount; level++)
			{
				// read all gpu formats
				for (int gpuFormat=0; gpuFormat < 3; gpuFormat++)
				{
					// read block length
					uint blockLength = readUInt24(data);
					if ((data.position + blockLength) > data.length) {
						throw new System.IO.InvalidDataException("Block length exceeds ATF file length");
					}

					if (blockLength > 0) {
#if PLATFORM_MONOTOUCH
						// handle PVRTC on iOS
						if (gpuFormat == 1) {
							OpenTK.Graphics.ES20.PixelInternalFormat pixelFormat = (OpenTK.Graphics.ES20.PixelInternalFormat)0x8C02;

							fixed(byte *ptr = data.getRawArray()) 
							{
								// upload from data position
								var address = new IntPtr(ptr + data.position);
								GL.CompressedTexImage2D(textureTarget, level, pixelFormat, width, height, 0, (int)blockLength, address);
							}
						}
#endif
						// TODO handle other formats/platforms
					}

					// next block data
					data.position += blockLength;
				}
			}
		}

		public void uploadCompressedTextureFromByteArray (ByteArray data, uint byteArrayOffset, bool async = false)
		{
			// $$TODO 
			// this is empty for now
#if PLATFORM_MONOMAC
			System.Console.WriteLine("NotImplementedWarning: Texture.uploadCompressedTextureFromByteArray()");

			if (!mDidUpload) {
				var clearData = new BitmapData(32,32, true, sColors[sColor % sColors.Length]);
				sColor++; 
				uploadFromBitmapData(clearData);
				clearData.dispose();
				mDidUpload = true;
			}
#endif

			// see if this is an ATF container
			data.position = byteArrayOffset;
			string signature = data.readUTFBytes(3);
			data.position = byteArrayOffset;
			if (signature == "ATF")
			{
				// Bind the texture
				GL.BindTexture (textureTarget, textureId);
				uploadATFTextureFromByteArray(data, byteArrayOffset);
				GL.BindTexture (textureTarget, 0);

				if (async) {
					dispatchDelayedTextureReady();
				}

				return;
			}


#if PLATFORM_MONOTOUCH
			int memUsage = (mWidth * mHeight) / 2;
			sMemoryUsedForTextures += memUsage;
			Console.WriteLine("Texture.uploadCompressedTextureFromByteArray() - " + mWidth + "x" + mHeight + " - Mem: " + (memUsage / 1024) + " KB - Total Mem: " + (sMemoryUsedForTextures / 1024) + " KB");

			// Bind the texture
			GL.BindTexture (textureTarget, textureId);

			if (byteArrayOffset != 0) {
				throw new NotSupportedException();
			}

			int dataLength = (int)(data.length - byteArrayOffset) - 4;		// We remove the 4 bytes footer
																			// TODO: Fix hardcoded value here

			OpenTK.Graphics.ES20.PixelInternalFormat pixelFormat = (OpenTK.Graphics.ES20.PixelInternalFormat)0x8C02;
			GL.CompressedTexImage2D(textureTarget, 0, pixelFormat, mWidth, mHeight, 0, dataLength, data.getRawArray());

			// unbind texture and pixel buffer
			GL.BindTexture (textureTarget, 0);
#endif
			if (async) {
				dispatchDelayedTextureReady();
			}
		}

		private void dispatchDelayedTextureReady() {
			// load with a delay
			var timer = new flash.utils.Timer(1, 1);
			timer.addEventListener(TimerEvent.TIMER, (System.Action<Event>)this.OnTextureReady );
			timer.start();
		}

		private void OnTextureReady (Event e)
		{
			this.dispatchEvent(new Event(Event.TEXTURE_READY)  );
		}
		
		public void uploadFromBitmapData (BitmapData source, uint miplevel = 0, bool generateMipmap = false)
		{
			int memUsage = (mWidth * mHeight) * 4;
			sMemoryUsedForTextures += memUsage;
			Console.WriteLine("Texture.uploadFromBitmapData() - " + mWidth + "x" + mHeight + " - Mem: " + (memUsage / 1024) + " KB - Total Mem: " + (sMemoryUsedForTextures / 1024) + " KB");

			// Bind the texture
			GL.BindTexture (textureTarget, textureId);

#if PLATFORM_MONOMAC
            if (generateMipmap) {
                GL.TexParameter (TextureTarget.Texture2D, TextureParameterName.GenerateMipmap, 1);
            }
#endif
#if PLATFORM_MONOMAC || PLATFORM_MONOTOUCH
			GL.TexImage2D(textureTarget, (int)miplevel, PixelInternalFormat.Rgba, mWidth, mHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte,source.getRawData());
#elif PLATFORM_MONODROID
			GL.TexImage2D<uint>(textureTarget, (int)miplevel, (int) PixelInternalFormat.Rgba, mWidth, mHeight, 0, PixelFormat.Rgba, PixelType.UnsignedByte, source.getRawData());
#endif

#if PLATFORM_MONOTOUCH || PLATFORM_MONODROID
            GL.GenerateMipmap(textureTarget);
#endif

			// unbind texture and pixel buffer
			GL.BindTexture (textureTarget, 0);

			source.dispose();
		}
		
		public void uploadFromByteArray(ByteArray data, uint byteArrayOffset, uint miplevel = 0) {
			throw new NotImplementedException();
		}

		public int width 	{ get { return mWidth; } }
		public int height 	{ get { return mHeight; } }
		
		
		private readonly Context3D 	mContext;
		private readonly int 		mWidth;
		private readonly int 		mHeight;
		private readonly string 	mFormat;
		private readonly bool 		mOptimizeForRenderToTexture;
		private readonly int    	mStreamingLevels;

#else

		public Texture(Context3D context, int width, int height, string format, 
		               bool optimizeForRenderToTexture, int streamingLevels)
		{
			throw new NotImplementedException();
		}
		
		public void uploadCompressedTextureFromByteArray(ByteArray data, uint byteArrayOffset, bool async = false) {
			throw new NotImplementedException();
		}
		
		public void uploadFromBitmapData (BitmapData source, uint miplevel = 0)
		{
			throw new NotImplementedException();
		}
		
		public void uploadFromByteArray(ByteArray data, uint byteArrayOffset, uint miplevel = 0) {
			throw new NotImplementedException();
		}

#endif

	}
	
}
