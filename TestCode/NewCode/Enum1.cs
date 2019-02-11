using System;

public namespace RootNamespace 
{
    public enum ShortEnum : short
    {
        Zero=0, One=1, Two=2
    }
    public enum SimpleEnum
    {
        Unknown=-1, One=1, /*Two=2,*/ Three = 3
    }

    [Flags]
    public enum FlagsEnum
    {
        Zero = 0, One = 1, Two = 2, Four = 4, All = 255
    }
}