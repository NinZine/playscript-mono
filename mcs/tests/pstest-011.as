package
{

	// Test calls to static methods with instance variables

	public class Foo {

		public static function bar():void {
		}

		public function blah():void {
			this.bar();
		}

	}

	public class Test 
	{

		public static function Main():void {
			var f:Foo = new Foo();
			f.bar();
		}
	}
}

