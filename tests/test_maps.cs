using System;           
using System.IO;
using System.Collections.Generic;
using bhl;

public class TestMaps : BHL_TestBase
{
  [IsTested()]
  public void TestSimpleDeclare()
  {
    string bhl = @"
    [string]int m = {}
    ";

    var c = Compile(bhl);

    var expected = 
      new ModuleCompiler()
      .UseInit()
      .EmitThen(Opcodes.New, new int[] { ConstIdx(c, c.ns.TMap("string", "int")) }) 
      .EmitThen(Opcodes.SetVar, new int[] { 0 })
    ;

    AssertEqual(c, expected);
  }

  [IsTested()]
  public void TestCountEmpty()
  {
    string bhl = @"

    func int test() 
    {
      [string]int m = {}
      return m.Count
    }
    ";

    var vm = MakeVM(bhl);
    AssertEqual(0, Execute(vm, "test").result.PopRelease().num);
    CommonChecks(vm);
  }

  //TODO:
  //[IsTested()]
  public void TestSimpleReadWrite()
  {
    string bhl = @"

    func int test() 
    {
      [string]int m = {}
      m[""hey""] = 42
      return m[""hey""]
    }
    ";

    var vm = MakeVM(bhl);
    AssertEqual(42, Execute(vm, "test").result.PopRelease().num);
    CommonChecks(vm);
  }
}
