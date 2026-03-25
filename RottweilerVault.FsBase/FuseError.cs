namespace RottweilerVault.FsBase;

public enum FuseError
{
    Success = 0,
    NoPermission = -1, //EPERM
    NoEntry = -2, //ENOENT
    IoError = -5, //EIO
    AccessDenied = -13, //EACCESS
    ResourceBusy = -16, //EBUSY
    AlreadyExists = -17, //EEXIST
    NotADirectory = -20, //ENOTDIR
    IsADirectory = -21, //EISDIR
    InvalidArgument = -22, //EINVAL
    TextFileBusy = -26, //ETXTBSY
    FileTooLarge = -27, //EFBIG
    NoDriveSpace = -28, //ENOSPC
    TooManyLinks = -31, //EMLINK
    FileNameTooLarge = -36, //ENAMETOOLONG
    DirectoryNotEmpty = -39, //ENOTEMPTY
    NoData = -61, //ENODATA
    BrokenSymLink = -67, //ENOLINK
    OperationNotSupported = -95, //EOPNOTSUPP
    ValueTooLarge = -139 //EOVERFLOW
}