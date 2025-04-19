Parses the linker and header files from a built [pret](https://github.com/pret) decomp into json objects.

### How to use:

1. Build the decomp - see the install.md in the associated directory.
2. Ensure the .map linker file is in the root of the decomp source directory.
3. Pass the path to the built decomp to the exe in /exe.
4. The program will create a directory named 'ClangParsed' in the supplied path containing the created .json files.
     Each global variable defined within a header file's scope will be placed in the associated .json file
     All structs definitions will be placed in a single `structs.json` file.
