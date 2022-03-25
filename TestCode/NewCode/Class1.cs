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

        MyClass() { }
        [Obsolete]
        MyClass(int obseleteOverload) { }
        public Task<int> ATaskReturningGenericMethod() { }
        public double ProtectedSetProperty { get; protected set; }
        public double WritableProperty { get; set; }
        public double ReadOnlyProperty { get; }
        public double SetOnlyProperty { set; }
        [Obsolete]
        public void AlreadyObsoletedMethod();
        [Obsolete]
        public void ObsoletedMethod();
        [Obsolete]
        public double ObsoletedProperty { set; }
        public event EventHandler SimpleEvent;
        public event EventHandler<double> GenericEvent;
        [Obsolete]
        public event EventHandler ObsoletedEvent { set; }
        public void Dispose() { }
        public override void SomeBaseMethod() {}
   }
   [Obsolete]
   public class ObsoletedClass {}
}