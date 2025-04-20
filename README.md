Parses the linker and header files from a built [pret](https://github.com/pret) decomp into json objects.
Uses the [ClangSharp](https://github.com/dotnet/clangsharp/) NuGet package, no local Clang installation required.

This application was slapped together in a couple hours and has a ton of issues, run at your own risk.

### How to use:

1. Build the decomp - see the install.md in the associated pret repo.
2. Ensure the .map linker file is in the root of the decomp source directory.
4. Pass the path to the built decomp to the exe in `RipDecomp.exe`.
5. The program will create a directory named 'ClangParsed' in the supplied path containing the created .json files.

### Notes:
1. Each global variable defined within a header file's scope will be placed in the associated .json file.
2. All structs definitions will be placed in a single `structs.json` file.
3. Ensure the `Clang-Include` directory is in the application working directory.
4. C standard libraries are NOT included by default - add any necessary library headers to the `Clang-Include` directory. `<stdint.h>` is provided, others may be found in the [Clang headers directory](https://clang.llvm.org/doxygen/dir_32af269ab941e393bd1c05d50cd12728.html).
5. Array dimensions where clang is unable to determine the size will be of size -1
6. By default, clang pointers are of size 8, however GBA pointers are of size 4. This may cause issues with sizing and offsets and may require custom implementation to overcome.
