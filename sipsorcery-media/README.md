Building
---------------------

Use `vcpkg` to install the dependencies.

- Clone `vcpkg` from the [github repository](https://github.com/Microsoft/vcpkg) and install as per the instructions in the main README.md.
- Install the required packages:

```
    PS >.\vcpkg install --triplet x64-windows openssl libvpx ffmpeg
```

Open `SIPSorcery.Media\SIPSorcery.Media.vcxproj` with Visual Studio.