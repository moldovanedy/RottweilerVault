namespace RottweilerVault.FsBase;

public struct TimestampData
{
    public long AccessTime;
    public long AccessTimeNanoseconds;

    public long ModifyTime;
    public long ModifyTimeNanoseconds;

    public bool ShouldUpdateAccessTime;
    public bool ShouldUpdateModifyTime;
}