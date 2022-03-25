using System;
using System.Threading.Tasks;

public namespace RootNamespace 
{
   public class BaseClass
   {
           public abstract void SomeBaseMethod();
   }
   
   public class MyClass : IDisposable
   {
        MyClass() { }
        MyClass(int obseleteOverload) { }
        public void AVoidMethod() { }
        public Task<int> ATaskReturningGenericMethod() { }
        public double ProtectedSetProperty { get; protected set; }
        public double WritableProperty { get; set; }
        public double ReadOnlyProperty { get; }
        public double SetOnlyProperty { set; }
        [Obsolete]
        public void AlreadyObsoletedMethod();
        public double ObsoletedProperty { set; }
        public void ObsoletedMethod();
        public event EventHandler SimpleEvent;
        public event EventHandler<double> GenericEvent;
        public event EventHandler ObsoletedEvent { set; }
        public void Dispose() { }
   }
   public class ObsoletedClass {}
}