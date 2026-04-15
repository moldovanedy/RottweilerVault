# RottweilerVault - a simple file vault that provides AES-XTS encryption and FUSE file system mounting

This tool allows you to create multiple volumes (large files with an internal file system), manage them, and mount them
anywhere (using FUSE on Linux). Everything is encrypted permanently, similarly to Microsoft BitLocker or LUKS. The
difference is that the volume is only active while this application is running, as opposed to being accessible anytime
the user is logged in.

## Supported platforms

For now, only Linux is supported (at least the mount option, which is essential for actually accessing files), as it
uses FUSE. You might get it running on other platforms if you install a FUSE library (like WinFUSE), but that's not
guaranteed.

## Usage examples

To create a new volume:

`rottweiler-vault create MyVolume ext2 --password Password123`

To mount the volume:

`rottweiler-vault mount MyVolume ~/mount_point --password Password123`

(mount_point is the path where the volume will be mounted, but it is optional; by default, it will be the path
of the volume file with the \"_data\" sufix; it must be an empty directory)

NOTE: Although you can specify the password as a command-line argument (like the above), it is safer to let
the application prompt you for the password (so you can write it to stdin), as that protects against leaking
the password in the terminal history.

IMPORTANT: The password is not stored anywhere. If you lose it, you will lose the access to your files.
Never lose your password (you can use a password manager for that).

## Limitations

- The maximum size of a volume is 64 GiB.
- Only the EXT2 file system is supported for now, which is susceptible to corruption (when the application
  abruptly closes / is closed) as it does not use journaling.
- No symlinks or hardlinks are supported yet.
- Only works with FUSE, which is Linux-specific.
