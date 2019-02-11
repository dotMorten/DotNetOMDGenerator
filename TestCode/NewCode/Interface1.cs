using System;
using System.Threading.Tasks;

public namespace RootNamespace 
{
   public interface Interface1
   {
	   void Method();
   }

   public interface DerivedInterface : Interface1
   {
	   void Method2();
   }
}
