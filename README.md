# RottweilerVault - an advanced file vault that provides AES-XTS encryption and FUSE file system mounting

This tool allows you to create multiple volumes (large files with an internal file system), manage them, and mount them
anywhere (using FUSE on Linux). Everything is encrypted permanently, similarly to Microsoft BitLocker or LUKS. The
difference is that the volume is only active while this application is running, as opposed to being accessible anytime
the system is running.

## Supported platforms

For now, only Linux is supported (at least the mount option, which is essential for actually accessing files), as it
uses FUSE.

## Usage examples

To create a new volume:

`rottweiler-vault create MyVolume ext2 --password Password123`

To mount the volume:

`rottweiler-vault mount MyVolume ~/mount_point --password Password123`
