using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Antlr4.Runtime.Misc;
using Mono.Options;

namespace bhl {

public class CompileCmd : ICmd
{
  const int ERROR_EXIT_CODE = 2;

  public static void Usage(string msg = "")
  {
    Console.WriteLine("Usage:");
    Console.WriteLine("bhl compile --dir=<root src dir> [--files=<file>] --result=<result file> --tmp-dir=<tmp dir> --error=<err file> [--postproc_dll=<postproc dll path>] [-d] [--deterministic] [--module_fmt=<1,2>]");
    Console.WriteLine(msg);
    Environment.Exit(1);
  }

  public void Run(string[] args)
  {
    var files = new List<string>();

    string src_dir = "";
    string res_file = "";
    string tmp_dir = "";
    bool use_cache = true;
    string err_file = "";
    string postproc_dll_path = "";
    string userbindings_dll_path = "";
    int max_threads = 1;
    bool check_deps = true;
    bool deterministic = false;
    bool debug = false;
    ModuleBinaryFormat module_fmt = ModuleBinaryFormat.FMT_LZ4;

    var p = new OptionSet() {
      { "dir=", "source dir",
        v => src_dir = v },
      { "files=", "source files list",
        v => files.AddRange(File.ReadAllText(v).Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None)) },
      { "result=", "result file",
        v => res_file = v },
      { "tmp-dir=", "tmp dir",
        v => tmp_dir = v },
      { "C", "don't use cache",
        v => use_cache = v == null },
      { "N", "don't check import deps",
        v => check_deps = v == null },
      { "postproc-dll=", "posprocess dll path",
        v => postproc_dll_path = v },
      { "bindings-dll=", "bindings dll path",
        v => userbindings_dll_path = v },
      { "error=", "error file",
        v => err_file = v },
      { "deterministic=", "deterministic build",
        v => deterministic = v != null },
      { "threads=", "number of threads",
          v => max_threads = int.Parse(v) },
      { "d", "debug version",
        v => debug = v != null },
      { "module-fmt=", "binary module format",
        v => module_fmt = (ModuleBinaryFormat)int.Parse(v) }
     };

    var extra = new List<string>();
    try
    {
      extra = p.Parse(args);
    }
    catch(OptionException e)
    {
      Usage(e.Message);
    }
    files.AddRange(extra);

    if(!Directory.Exists(src_dir))
      Usage("Root source directory is not valid");
    src_dir = Path.GetFullPath(src_dir);

    if(res_file == "")
      Usage("Result file path not set");

    if(tmp_dir == "")
      Usage("Tmp dir not set");

    IUserBindings userbindings = new EmptyUserBindings();
    if(userbindings_dll_path != "")
    {
      var userbindings_assembly = System.Reflection.Assembly.LoadFrom(userbindings_dll_path);
      var userbindings_class = userbindings_assembly.GetTypes()[0];
      userbindings = System.Activator.CreateInstance(userbindings_class) as IUserBindings;
      if(userbindings == null)
        Usage("User bindings are invalid");
    }

    IFrontPostProcessor postproc = new EmptyPostProcessor();
    if(postproc_dll_path != "")
    {
      var postproc_assembly = System.Reflection.Assembly.LoadFrom(postproc_dll_path);
      var postproc_class = postproc_assembly.GetTypes()[0];
      postproc = System.Activator.CreateInstance(postproc_class) as IFrontPostProcessor;
      if(postproc == null)
        Usage("User postprocessor is invalid");
    }

    if(files.Count == 0)
      CompilationExecutor.AddFilesFromDir(src_dir, files);

    for(int i=files.Count;i-- > 0;)
    {
      if(string.IsNullOrEmpty(files[i]))
        files.RemoveAt(i);
    }

    if(deterministic)
      files.Sort();

    Console.WriteLine("BHL total files: {0}(debug: {1}, cache: {2})", files.Count, debug, use_cache);
    var conf = new CompileConf();
    conf.args = string.Join(";", args);
    conf.module_fmt = module_fmt;
    conf.use_cache = use_cache;
    conf.self_file = GetSelfFile();
    conf.check_deps = check_deps;
    conf.files = files;
    conf.inc_dir = src_dir;
    conf.max_threads = max_threads;
    conf.res_file = res_file;
    conf.tmp_dir = tmp_dir;
    conf.err_file = err_file;
    conf.userbindings = userbindings;
    conf.postproc = postproc;
    conf.debug = debug;
    conf.verbose = true;

    var cmp = new CompilationExecutor();
    var err = cmp.Exec(conf);
    if(err != null)
    {
      if(string.IsNullOrEmpty(err_file))
        ErrorUtils.OutputError(err.file, err.line, err.char_pos, err.text);
      Environment.Exit(ERROR_EXIT_CODE);
    }
  }

  public static string GetSelfFile()
  {
    return System.Reflection.Assembly.GetExecutingAssembly().Location;
  }
}

}
