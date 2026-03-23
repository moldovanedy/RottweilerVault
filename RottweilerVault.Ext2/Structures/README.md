# Layout of a volume

1 block = 4096 bytes

There can be a maximum of 0x400_000 inodes and 0x1_000_000 blocks in a volume.

A block group is described by:

1. Superblock (copy or original if it's the first group) = 1 block
2. Block groups descriptor table (copy or original if it's the first group) = 4 blocks
3. Block bitmap = 1 block
4. Inode bitmap = 1 block
5. Inode table = 256 blocks
6. Data blocks = 32505 blocks

## Block groups descriptor table

The block groups descriptor table is an array of 512 entries, allowing a maximum volume size of 64 GiB.
This is because each descriptor is 32 bytes, so that's why we allocate 4 blocks.

# Inode table

We use 4096 * 2 = 8192 inodes per block group. Each inode is 128 bytes.