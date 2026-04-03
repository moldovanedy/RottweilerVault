using System.Collections.Generic;

namespace RottweilerVault.FsBase.FsStructures;

public class FsDirectory : FsInode
{
    private readonly Dictionary<string, FsInode> _directDescendants = [];

    public Dictionary<string, FsInode>.Enumerator GetEnumerator()
    {
        return _directDescendants.GetEnumerator();
    }

    public FsInode? GetEntryOrNull(string name)
    {
        return _directDescendants.GetValueOrDefault(name);
    }

    public bool TryAdd(FsInode newEntry)
    {
        return _directDescendants.TryAdd(newEntry.Name, newEntry);
    }

    public bool Remove(string name)
    {
        return _directDescendants.Remove(name);
    }

    public void ClearDescendants()
    {
        _directDescendants.Clear();
    }

    public FsInode this[string name]
    {
        get => _directDescendants[name];
        set => _directDescendants[name] = value;
    }
}