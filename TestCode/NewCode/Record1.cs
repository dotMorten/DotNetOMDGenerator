using System;
using System.Threading.Tasks;

public namespace RootNamespace 
{
	public abstract record Action;
	public record Add(string Text) : Action;
}