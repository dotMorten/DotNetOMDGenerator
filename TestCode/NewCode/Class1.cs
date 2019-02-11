using System;
using System.Threading.Tasks;

public namespace RootNamespace 
{
   public class BaseClass
   {
	   public abstract void SomeBaseMethod();
       public void AVoidMethod() { }
   }
   
   public class MyClass : BaseClass, IDisposable
   {
        public Task<int> ATaskReturningGenericMethod() { }
        public double ProtectedSetProperty { get; protected set; }
        public double WritableProperty { get; set; }
        public double ReadOnlyProperty { get; }
        public double SetOnlyProperty { set; }
		public event EventHandler SimpleEvent;
		public event EventHandler<double> GenericEvent;
		public void Dispose() { }
		public override void SomeBaseMethod() {}
   }
}