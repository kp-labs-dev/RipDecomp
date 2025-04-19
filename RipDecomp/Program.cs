namespace ClangTest
{
    using ClangSharp;
    using ClangSharp.Interop;
    using System.Text.RegularExpressions;
    using static ClangSharp.Interop.clang;

    public class DecompLib
    {
        public DecompLib(string name)
        {
            Name = name;
        }
        public string Name { get; }
        public Dictionary<string, DecompStruct> Structs { get; set; } = new();
        public Dictionary<string, DecompVar> Vars { get; set; } = new();

    }

    public class DecompVar
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsPointer { get; set; } = false;
        public int[] ArrayDims { get; set; } = Array.Empty<int>();
        public int Offset { get; set; } = 0;
        public int Size { get; set; } = 0;
        public int TypeSize { get; set; } = 0;
    }
    public class DecompStruct
    {
        public string Name { get; set; } = string.Empty;
        public int Size { get; set; } = 0;
        public bool IsUnion { get; set; } = false;
        public Dictionary<string, DecompStructField> Fields { get; set; } = new();

        public override string ToString()
        {
            return $"{(IsUnion ? "(Union) " : "")}{Name}\n\t{string.Join("\n\t", Fields.Values)}";
        }
    }

    public class DecompStructField
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public int Offset { get; set; } = 0x0;
        public int Size { get; set; } = 0;
        public int TypeSize { get; set; } = 0;
        public int FieldBits { get; set; } = 0;
        public bool IsPointer { get; set; } = false;
        public int[] ArrayDims { get; set; } = Array.Empty<int>();

        public override string ToString()
        {
            string name = Name;
            if (ArrayDims.Length > 0)
            {
                name += $"[{string.Join("][", ArrayDims)}]";
            }

            if (FieldBits >= 0)
            {
                name += $":{FieldBits}";
            }

            return $"{Type} {name} ({Offset})";
        }
    }

    public class DecompFieldUnion : DecompStructField
    {
        public Dictionary<string, DecompStructField> Fields { get; set; } = new();
    }

    public class ClangTest
    {
        class HeaderFile
        {
            public string FullPath { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string Contents { get; set; } = string.Empty;
            public int Length { get; set; } = 0;
            public string[] Includes { get; set; } = Array.Empty<string>();

            public HeaderFile(string path)
            {
                var info = new FileInfo(path);

                FullPath = info.FullName;
                Name = info.Name;
                Contents = File.ReadAllText(info.FullName);
                Length = Contents.Length;
                Includes = GetIncludes(Contents);
            }

            static string[] GetIncludes(string file)
            {
                var matches = Regex.Matches(file, "#include \"(.*\\.h)\"");
                return matches.Select(m => m.Groups[1].Value).ToArray();
            }
        }

        static string RootDir = string.Empty;

        static string Include = ".\\Clang-Include";

        static Dictionary<string, HeaderFile> Headers = new Dictionary<string, HeaderFile>();
        public static Dictionary<string, DecompLib> Libs = new();

        public static Dictionary<string, DecompVar> Vars = new();
        public static Dictionary<string, DecompVar> ExternVars = new();
        static Dictionary<string, string> VarReference = new();

        static Dictionary<string, DecompStruct> Structs = new();
        static Dictionary<string, string> StructReference = new();

        static Dictionary<string, int> VarOffsets = GetOffsets();
        public static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Please specify a directory");
                return;
            }

            RootDir = args[0];

            if (!Directory.Exists(RootDir))
            {
                Console.WriteLine($"Directory '{RootDir}' not found");
                return;
            }

            Headers = new Dictionary<string, HeaderFile>(GetHeaderFiles());
            
            foreach(var file in Headers)
            {
                try
                {
                    ReadHeaderFile(file.Value);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            Libs = BuildLibs().OrderByDescending(l => l.Key).ToDictionary();

            var structs = new Dictionary<string, DecompStruct>();
            var dir = Directory.CreateDirectory(Path.Combine(RootDir, "ClangParsed"));

            foreach (var lib in Libs)
            {
                foreach(var str in lib.Value.Structs)
                {
                    str.Value.Fields = str.Value.Fields.OrderBy(f => f.Value.Offset).ToDictionary();
                    structs.Add(str.Key, str.Value);
                }

                lib.Value.Vars = lib.Value.Vars.OrderBy(v => v.Value.Name).ToDictionary();

                if (lib.Value.Vars.Count > 0)
                {
                    File.WriteAllText(
                        Path.Combine(
                            dir.FullName, 
                            $"{lib.Key}.json"), 
                        Newtonsoft.Json.JsonConvert.SerializeObject(
                            lib.Value.Vars, 
                            Newtonsoft.Json.Formatting.Indented));
                }
            }

            File.WriteAllText(Path.Combine(dir.FullName, "structs.json"), Newtonsoft.Json.JsonConvert.SerializeObject(structs, Newtonsoft.Json.Formatting.Indented));

            int libCount = Libs.Count;
            int structCount = Libs.Values.Select(l => l.Structs.Count).Sum();
            int varCount = Libs.Values.Select(l => l.Vars.Count).Sum();

            Console.WriteLine($"Parsed {libCount} libraries, {structCount} structs, {varCount} vars");

            var libs = Libs;

            Console.ReadKey();
        }


        static IEnumerable<KeyValuePair<string, HeaderFile>> GetHeaderFiles()
        {
            foreach (var dir in Directory.EnumerateDirectories(RootDir))
            {
                foreach (var file in GetDirHeaderFiles(dir))
                {
                    yield return file;
                }
            }
        }

        static Dictionary<string, HeaderFile> GetDirHeaderFiles(string path, string prefix = "")
        {
            Dictionary<string, HeaderFile> headers = new();
            var dir = new DirectoryInfo(path);
            foreach(var file in dir.EnumerateFiles())
            {
                if (!file.Name.EndsWith(".h"))
                {
                    continue;
                }

                var key = prefix + file.Name;
                headers.Add(key, new HeaderFile(file.FullName));
            }

            foreach(var subDir in dir.EnumerateDirectories())
            {
                foreach(var subFile in GetDirHeaderFiles(subDir.FullName, subDir.Name + "/"))
                {
                    headers.Add(subFile.Key, subFile.Value);
                }
            }

            return headers;
        }

        static void ReadHeaderFile(HeaderFile file)
        {
            Console.WriteLine(file.Name);
            unsafe
            {
                var idx = createIndex(0, 0);
                var args = new Span<string>(["-std-c11", "-include", Path.Combine(RootDir, "include", "global.h"), $"-I{Include}"]);
                var unit = CXTranslationUnit.CreateFromSourceFile(CXIndex.Create(), file.FullPath, args, default);

                unit.Cursor.VisitChildren(GetVisitor(), default);

                unit.Dispose();
            }
        }

        static Dictionary<string, DecompLib> BuildLibs()
        {
            Dictionary<string, DecompLib> libs = new();
            var vars = Vars;
            foreach (var structRef in StructReference)
            {
                if (!libs.TryGetValue(structRef.Value, out DecompLib? lib))
                {
                    lib = new DecompLib(structRef.Value);
                    libs.Add(lib.Name, lib);
                }

                lib.Structs.Add(structRef.Key, Structs[structRef.Key]);
            }

            foreach(var varRef in VarReference)
            {
                if (!libs.TryGetValue(varRef.Value, out DecompLib? lib))
                {
                    lib = new DecompLib(varRef.Value);
                    libs.Add(lib.Name, lib);
                }

                if (Vars.ContainsKey(varRef.Key))
                {
                    lib.Vars.Add(varRef.Key, Vars[varRef.Key]);
                }
                else if (ExternVars.ContainsKey(varRef.Key))
                {
                    lib.Vars.Add(varRef.Key, ExternVars[varRef.Key]);
                }
            }

            foreach(var lib in new Dictionary<string, DecompLib>(libs))
            {
                if (lib.Value.Vars.Count == 0 && lib.Value.Structs.Count == 0)
                {
                    libs.Remove(lib.Key);
                }
            }

            return libs;
        }

        static string? GetMapFile()
        {
            var found = FindFiles(f => f.EndsWith(".map"));
            return found.Length > 0 ? found[0] : null;
        }

        static string[] FindFiles(Func<string, bool> predicate, string? searchDir = null, bool breakOnFound = false)
        {
            List<string> found = new List<string>();
            var dir = new DirectoryInfo(searchDir ?? RootDir);

            if (!dir.Exists)
            {
                return Array.Empty<string>();
            }

            foreach(var f in dir.GetFiles())
            {
                bool isMatch = predicate(f.FullName);

                if (!isMatch)
                {
                    continue;
                }

                found.Add(f.FullName);

                if (isMatch && breakOnFound)
                {
                    break;
                }
            }

            foreach(var d in dir.GetDirectories())
            {
                if (found.Count > 0 && breakOnFound)
                {
                    break;
                }

                found.AddRange(FindFiles(predicate, d.FullName, breakOnFound));
            }

            return found.ToArray();
        }

        static Dictionary<string, int> GetOffsets()
        {
            Dictionary<string, int> offsets = new();

            var mapPath = GetMapFile();
            
            if (mapPath != null)
            {
                var mapFile = File.ReadAllText(mapPath);
                var rx = @"(0x[0-9a-f]{16})(?: +(?!0x))(\w+)";
                var matches = Regex.Matches(mapFile, rx);

                foreach(Match match in matches)
                {
                    var offset = Convert.ToInt32(match.Groups[1].Value, fromBase: 16);
                    var name = match.Groups[2].Value;

                    offsets.Add(name, offset);
                }

                Console.WriteLine($"Read {offsets.Count} offsets");
            }
            else
            {
                Console.WriteLine("No .map file found, variable offsets will not be applied");
            }

            return offsets;
        }

        static unsafe string GetCursorLibrary(CXCursor cursor)
        {
            cursor.Location.GetSpellingLocation(out CXFile file, out uint line, out uint column, out uint offset);
            var loc = file.TryGetRealPathName().CString;

            return Path.GetFileNameWithoutExtension(loc);
        }

        static unsafe void ParseChild(CXCursor cursor)
        {
            var kind = getCursorKind(cursor);

            switch(kind)
            {
                case CXCursorKind.CXCursor_StructDecl:
                case CXCursorKind.CXCursor_ClassDecl:
                    {
                        ParseStruct(cursor); break;
                    }
                case CXCursorKind.CXCursor_UnionDecl:
                    {
                        ParseStruct(cursor, true); break;
                    }
                case CXCursorKind.CXCursor_VarDecl:
                    {
                        ParseVar(cursor); break;
                    }
            }
        }

        static unsafe void ParseVar(CXCursor cursor)
        {
            string name = getCursorSpelling(cursor).CString;
            bool isExtern = cursor.StorageClass == CX_StorageClass.CX_SC_Extern;

            if ((isExtern && ExternVars.ContainsKey(name)) || (!isExtern && Vars.ContainsKey(name)))
            {
                return;
            }

            var parsed = BuildVar(cursor);
            if (parsed == null)
            {
                return;
            }

            if (isExtern)
            {
                VarReference[parsed.Name] = GetCursorLibrary(cursor);
                ExternVars.Add(parsed.Name, parsed);
            }
            else
            {
                if (!VarReference.ContainsKey(parsed.Name))
                {
                    VarReference[parsed.Name] = GetCursorLibrary(cursor);
                }

                Vars.Add(parsed.Name, parsed);
            }
        }

        static unsafe DecompVar? BuildVar(CXCursor cursor)
        {
            string name = getCursorSpelling(cursor).CString;

            var varType = getCursorType(cursor);

            var size = (int)varType.SizeOf;
            var arrDims = Array.Empty<int>();

            if (varType.kind == CXTypeKind.CXType_ConstantArray || varType.kind == CXTypeKind.CXType_IncompleteArray)
            {
                var arrInfo = GetArrayInfo(varType, cursor);

                arrDims = arrInfo.Dims;
                varType = arrInfo.Type;
            }

            var isPointer = varType.kind == CXTypeKind.CXType_Pointer;
            if (isPointer)
            {
                varType = getPointeeType(varType);
            }

            var typeName = getCursorSpelling(getTypeDeclaration(varType)).CString;
            var typeSize = (int)varType.SizeOf;

            size = size < 0 ? typeSize : size;

            return new DecompVar
            {
                Name = name,
                Size = size,
                Type = typeName,
                IsPointer = isPointer,
                TypeSize = typeSize,
                ArrayDims = arrDims,
                Offset = VarOffsets.TryGetValue(name, out int offset) ? offset : 0
            };
        }

        static unsafe void ParseStruct(CXCursor cursor, bool isUnion = false)
        {
            var parsed = BuildStruct(cursor, isUnion);
            if (parsed == null)
            {
                return;
            }

            StructReference[parsed.Name] = GetCursorLibrary(cursor);
            Structs.Add(parsed.Name, parsed);
        }

        static unsafe DecompStruct? BuildStruct(CXCursor cursor, bool isUnion = false)
        {
            string name = getCursorSpelling(cursor).CString;

            if (Structs.ContainsKey(name))
            {
                return null;
            }

            var cxType = getCursorType(cursor);
            int size = (int)cxType.SizeOf;

            return new DecompStruct
            {
                Name = name,
                Size = (int)size,
                IsUnion = isUnion,
                Fields = GetFields(cursor)
            };
        }

        static unsafe Dictionary<string, DecompStructField> GetFields(CXCursor cursor)
        {
            Dictionary<string, DecompStructField> fields = new();

            cursor.VisitChildren(visitor: (CXCursor child, CXCursor parent, void* clientData) =>
            {
                if (getCursorKind(child) == CXCursorKind.CXCursor_FieldDecl)
                {
                    var field = BuildStructField(child);
                    if (field != null)
                    {
                        fields.TryAdd(field.Name, field);
                    }
                }

                return CXChildVisitResult.CXChildVisit_Continue;

            }, default);

            return fields;
        }

        static unsafe DecompStructField? BuildStructField(CXCursor cursor)
        {
            try
            {
                CXType fieldType = getCursorType(cursor);
                CXTypeKind typeKind = fieldType.kind;

                var cSize = Type_getSizeOf(fieldType);
                var cAlign = Type_getAlignOf(fieldType);

                var name = getCursorSpelling(cursor).CString;
                bool isPointer = fieldType.kind == CXTypeKind.CXType_Pointer;
                var fieldoffset = (int)cursor.OffsetOfField;
                var width = (int)clang.getFieldDeclBitWidth(cursor);
                var arrayDims = Array.Empty<int>();
                var size = (int)fieldType.SizeOf;

                if (isPointer)
                {
                    fieldType = getPointeeType(fieldType);
                    typeKind = fieldType.kind;
                }

                if (typeKind == CXTypeKind.CXType_ConstantArray)
                {
                    var arrInfo = GetArrayInfo(fieldType, cursor);

                    fieldType = arrInfo.Type;
                    typeKind = fieldType.kind;
                    arrayDims = arrInfo.Dims;
                }

                var typeName = (getCursorKind(getTypeDeclaration(fieldType)) == CXCursorKind.CXCursor_UnionDecl)
                    ? "union"
                    : getTypeSpelling(fieldType).CString;

                typeName = (getCursorKind(getTypeDeclaration(fieldType)) == CXCursorKind.CXCursor_StructDecl)
                    ? getCursorSpelling(getTypeDeclaration(fieldType)).CString
                    : typeName;

                var typeSize = (int)fieldType.SizeOf;

                cursor.Location.GetSpellingLocation(out CXFile file, out uint line, out uint column, out uint offset);
                var loc = file.TryGetRealPathName().CString;

                return (typeName == "union")
                    ? new DecompFieldUnion
                    {
                        Name = name,
                        Type = typeName,
                        Size = size,
                        Offset = fieldoffset,
                        FieldBits = width,
                        IsPointer = isPointer,
                        TypeSize = typeSize,
                        ArrayDims = arrayDims,
                        Fields = GetFields(cursor)
                    }
                    : new DecompStructField
                    {
                        Name = name,
                        Type = typeName,
                        Size = size,
                        Offset = fieldoffset,
                        FieldBits = width,
                        IsPointer = isPointer,
                        TypeSize = typeSize,
                        ArrayDims = arrayDims
                    };
            }
            catch
            {
                return null;
            }
        }

        static unsafe (int[] Dims, CXType Type) GetArrayInfo(CXType type, CXCursor cursor)
        {
            if (type.kind == CXTypeKind.CXType_ConstantArray)
            {
                int[] sizes = [(int)getArraySize(type)];
                var elementType = getArrayElementType(type);

                var next = GetArrayInfo(elementType, cursor);

                return (sizes.Concat(next.Dims).ToArray(), next.Type);
            }
            else if (type.kind == CXTypeKind.CXType_IncompleteArray)
            {
                int[] sizes = [-1];
                var elementType = getArrayElementType(type);

                var next = GetArrayInfo(elementType, cursor);

                return (sizes.Concat(next.Dims).ToArray(), next.Type);
            }
            else
            {
                return (Array.Empty<int>(), type);
            }
        }

        static CXCursorVisitor GetVisitor()
        {
            unsafe
            {
                return (CXCursor cursor, CXCursor parent, void* clientData) =>
                {
                    ParseChild(cursor);

                    return CXChildVisitResult.CXChildVisit_Continue;
                };
            }
        }
    }
}
