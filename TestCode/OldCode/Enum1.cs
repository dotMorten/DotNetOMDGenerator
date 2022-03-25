using System;

public namespace RootNamespace 
{
    public enum ShortEnum : long
    {
        Zero=0, One=1, Two=2
    }
    public enum SimpleEnum
    {
        Unknown=0, One=1, Two=2
    }

    [Flags]
    public enum FlagsEnum
    {
        Zero = 0,
	One = 1,
	Two = 2,
	Four = 4,
	Obsolete = 5,
	All = 255,
    }
}