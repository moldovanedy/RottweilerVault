using System;
using System.Collections;
using System.Collections.Generic;

namespace RottweilerVault.FsBase.FsStructures;

public class FsDirectoryEnumerator : IEnumerator<FsInode?>
{
    public FsInode? Current { get; private set; }

    object? IEnumerator.Current => Current;

    private readonly Func<FsInode?> _requestFunction;
    private readonly Action _resetFunction;

    public FsDirectoryEnumerator(Func<FsInode?> requestFunction, Action resetFunction)
    {
        _requestFunction = requestFunction;
        _resetFunction = resetFunction;
    }

    public bool MoveNext()
    {
        Current = _requestFunction();
        return Current != null;
    }

    public void Reset()
    {
        _resetFunction();
        Current = null;
    }


    public void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}