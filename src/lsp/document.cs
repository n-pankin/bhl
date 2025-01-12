using System;
using System.Collections.Generic;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using bhl;

namespace bhlsp
{
  public abstract class BHLSPTextDocument
  {
    public Uri uri { get; set; }
    public string text { get; set; }
    
    List<int> indices = new List<int>();
    
    public virtual void Sync(string text)
    {
      this.text = text;
      ComputeIndexes();
    }

    void ComputeIndexes()
    {
      indices.Clear();
      
      int cur_index = 0;
      int cur_line = 0;
      int cur_col = 0;
      
      indices.Add(cur_index);

      int length = text.Length;
      // Go through file and record index of start of each line.
      for(int i = 0; i < length; ++i)
      {
        if(cur_index >= length)
          break;

        char ch = text[cur_index];
        if(ch == '\r')
        {
          if(cur_index + 1 >= length)
            break;
          
          if(text[cur_index + 1] == '\n')
          {
            cur_line++;
            cur_col = 0;
            cur_index += 2;
            indices.Add(cur_index);
          }
          else
          {
            // Error in code.
            cur_line++;
            cur_col = 0;
            cur_index += 1;
            indices.Add(cur_index);
          }
        }
        else if(ch == '\n')
        {
          cur_line++;
          cur_col = 0;
          cur_index += 1;
          indices.Add(cur_index);
        }
        else
        {
          cur_col += 1;
          cur_index += 1;
        }
        
        if(cur_index >= length)
          break;
      }
    }

    public int GetIndex(int line, int column)
    {
      if(indices.Count > 0 && line < indices.Count)
        return indices[line] + column;

      return 0;
    }
    
    public int GetIndex(int line)
    {
      if(indices.Count > 0 && line < indices.Count)
        return indices[line];

      return 0;
    }
    
    public (int, int) GetLineColumn(int index)
    {
      // Binary search.
      int low = 0;
      int high = indices.Count - 1;
      int i = 0;
      
      while (low <= high)
      {
        i = (low + high) / 2;
        var v = indices[i];
        if (v < index) low = i + 1;
        else if (v > index) high = i - 1;
        else break;
      }
      
      var min = low <= high ? i : high;
      return (min, index - indices[min]);
    }
  }

  public class JSTextDocument : BHLSPTextDocument
  {
    public override void Sync(string text)
    {
      base.Sync(text);
    }
  }
  
  public class BHLTextDocumentVisitor : bhlBaseVisitor<object>
  {
    public readonly List<string> imports = new List<string>();
    public readonly Dictionary<string, bhlParser.FuncDeclContext> funcDecls = new Dictionary<string, bhlParser.FuncDeclContext>();
    public readonly Dictionary<string, bhlParser.ClassDeclContext> classDecls = new Dictionary<string, bhlParser.ClassDeclContext>();
    public readonly List<uint> dataSemanticTokens = new List<uint>();
    
    private int next;
    private BHLTextDocument document;

    public void VisitDocument(BHLTextDocument document)
    {
      this.document = document;
      next = 0;
      
      imports.Clear();
      funcDecls.Clear();
      classDecls.Clear();
      dataSemanticTokens.Clear();
      
      VisitProgram(document.ToParser().program());
    }
    
    public override object VisitProgram(bhlParser.ProgramContext ctx)
    {
      for(var i=0;i<ctx.progblock().Length;++i)
        Visit(ctx.progblock()[i]);
      
      return null;
    }

    public override object VisitClassDecl(bhlParser.ClassDeclContext ctx)
    {
      var classDeclName = ctx.NAME();

      if(classDeclName != null)
      {
        var classDeclNameText = classDeclName.GetText();
        if(!classDecls.ContainsKey(classDeclNameText))
          classDecls.Add(classDeclNameText, ctx);
      }
      
      AddSemanticToken(ctx.Start.StartIndex, classDeclName.Symbol.StartIndex - 1, SemanticTokenTypes.keyword);
      
      AddSemanticToken(classDeclName, SemanticTokenTypes.@class);
      
      if(ctx.extensions() != null)
      {
        for(int i=0;i<ctx.extensions().nsName().Length;++i)
        {
          var ext_name = ctx.extensions().nsName()[i];
          AddSemanticToken(ext_name.dotName().NAME(), SemanticTokenTypes.@class);
        }
      }
      
      foreach(var classMember in ctx.classBlock().classMembers().classMember())
      {
        var classMemberVarDeclare = classMember.fldDeclare()?.varDeclare();
        if(classMemberVarDeclare != null)
        {
          Visit(classMemberVarDeclare.type());
          
          AddSemanticToken(classMemberVarDeclare.NAME(), SemanticTokenTypes.variable,
            SemanticTokenModifiers.definition, SemanticTokenModifiers.@static);
        }
      }

      return null;
    }
    
    public override object VisitFuncDecl(bhlParser.FuncDeclContext ctx)
    {
      var funcDeclName = ctx.NAME();
      var retType = ctx.retType();
      var funcParams = ctx.funcParams();
      var funcBlock = ctx.funcBlock();

      if(funcDeclName != null)
      {
        var funcDeclNameText = funcDeclName.GetText();
        if(!funcDecls.ContainsKey(funcDeclNameText))
          funcDecls.Add(funcDeclNameText, ctx);
      }
      
      var keywordStopIdx = retType?.Start.StartIndex ?? (funcDeclName?.Symbol.StartIndex ?? 0);

      AddSemanticToken(ctx.Start.StartIndex, Math.Max(ctx.Start.StartIndex, keywordStopIdx - 1),
        SemanticTokenTypes.keyword);
      
      if(retType != null)
      {
        foreach(var t in retType.type())
        {
          if(t.exception != null)
            continue;
          
          Visit(t);
        }
      }
      
      if(funcDeclName != null)
      {
        AddSemanticToken(funcDeclName, SemanticTokenTypes.function,
          SemanticTokenModifiers.definition, SemanticTokenModifiers.@static);
      }

      if(funcParams != null)
      {
        foreach(var funcParamDeclare in funcParams.funcParamDeclare())
          VisitFuncParamDeclare(funcParamDeclare);
      }

      if(funcBlock != null)
        Visit(funcBlock);
      
      return null;
    }
    
    public override object VisitFuncBlock(bhlParser.FuncBlockContext ctx)
    {
      var funcBlock = ctx.block();
      if(funcBlock != null)
        Visit(funcBlock);
      
      return null;
    }
    
    public override object VisitBlock(bhlParser.BlockContext ctx)
    {
      foreach(var statementItem in ctx.statement())
        Visit(statementItem);
      
      return null;
    }
    
    public override object VisitExpTypeCast(bhlParser.ExpTypeCastContext ctx)
    {
      var expTypeCastType = ctx.type();
      if(expTypeCastType != null)
        Visit(expTypeCastType);
      
      var expTypeCastExp = ctx.exp();
      if(expTypeCastExp != null)
        Visit(expTypeCastExp);
      
      return null;
    }
    
    public override object VisitLambdaCall(bhlParser.LambdaCallContext ctx)
    {
      var funcLambda = ctx.funcLambda();
      if(funcLambda != null)
        Visit(funcLambda);
      return null;
    }
    
    public override object VisitExpLambda(bhlParser.ExpLambdaContext ctx)
    {
      var funcLambda = ctx.funcLambda();
      if(funcLambda != null)
        Visit(funcLambda);
      return null;
    }

    public override object VisitFuncLambda(bhlParser.FuncLambdaContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Start.StartIndex+3, SemanticTokenTypes.keyword);
        
      var retType = ctx.retType();
      var funcParams = ctx.funcParams();
      var funcBlock = ctx.funcBlock();
      var chainExp = ctx.chainExp();

      if(retType != null)
      {
        foreach(var t in retType.type())
        {
          if(t.exception != null)
            continue;
          
          Visit(t);
        }
      }
        
      if(funcParams != null)
      {
        foreach(var funcParamDeclare in funcParams.funcParamDeclare())
          VisitFuncParamDeclare(funcParamDeclare);
      }
        
      if(funcBlock != null)
        Visit(funcBlock);
        
      if(chainExp != null)
      {
        foreach(var chainExpItem in chainExp)
          Visit(chainExpItem);
      }
      
      return null;
    }
    
    public override object VisitChainExp(bhlParser.ChainExpContext ctx)
    {
      if(ctx.callArgs() is bhlParser.CallArgsContext callArgs)
        Visit(callArgs);
      else if(ctx.memberAccess() is bhlParser.MemberAccessContext memberAccess)
        Visit(memberAccess);
      else if(ctx.arrAccess() is bhlParser.ArrAccessContext arrAccess)
        Visit(arrAccess);
      
      return null;
    }
    
    public override object VisitForeach(bhlParser.ForeachContext ctx)
    {
      var foreachExp = ctx.foreachExp();
      var foreachBlock = ctx.block();
      
      if(foreachExp != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, foreachExp.Start.StartIndex-1, SemanticTokenTypes.keyword);
        
        var exp = foreachExp.exp();
        if(exp != null)
          Visit(exp);
        
        //TODO: support multi-declares
        var varOrDeclare = foreachExp.varOrDeclares().varOrDeclare()[0];
        var varDeclare = varOrDeclare?.varDeclare();
        var varOrDeclareName = varOrDeclare?.NAME();
        
        if(varOrDeclareName != null)
          AddSemanticToken(varOrDeclareName, SemanticTokenTypes.variable);
        else if(varDeclare != null)
          VisitVarDeclare(varDeclare);
      }
      
      if(foreachBlock != null)
        Visit(foreachBlock);
      
      return null;
    }
    
    public override object VisitBreak(bhlParser.BreakContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.keyword);
      return null;
    }
    
    public override object VisitYield(bhlParser.YieldContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex-2, SemanticTokenTypes.keyword);
      return null;
    }
    
    public override object VisitYieldWhile(bhlParser.YieldWhileContext ctx)
    {
      var yieldWhileExp = ctx.exp();
      if(yieldWhileExp != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, yieldWhileExp.Start.StartIndex-2, SemanticTokenTypes.keyword);
        Visit(yieldWhileExp);
      }
      return null;
    }
    
    public override object VisitDefer(bhlParser.DeferContext ctx)
    {
      var deferBlock = ctx.block();
      
      if(deferBlock != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, deferBlock.Start.StartIndex-1, SemanticTokenTypes.keyword);
        Visit(deferBlock);
      }
      
      return null;
    }
    
    public override object VisitReturn(bhlParser.ReturnContext ctx)
    {
      var exps = ctx.returnVal()?.exps();
      if(exps != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, exps.Start.StartIndex-1, SemanticTokenTypes.keyword);
        
        foreach(var exp in exps.exp())
          Visit(exp);
      }
      
      return null;
    }
    
    public override object VisitExpMulDivMod(bhlParser.ExpMulDivModContext ctx)
    {
      var operatorMulDivMod = ctx.operatorMulDivMod();
      var operatorMulDivModExpLeft = ctx.exp(0);
      var operatorMulDivModRight = ctx.exp(1);
      
      if(operatorMulDivMod != null)
        AddSemanticToken(operatorMulDivMod.Start.StartIndex, operatorMulDivMod.Stop.StopIndex, SemanticTokenTypes.@operator);

      if(operatorMulDivModExpLeft != null)
        Visit(operatorMulDivModExpLeft);
      
      if(operatorMulDivModRight != null)
        Visit(operatorMulDivModRight);
      
      return null;
    }
    
    public override object VisitExpCompare(bhlParser.ExpCompareContext ctx)
    {
      var operatorComparison = ctx.operatorComparison();
      var operatorComparisonExpLeft = ctx.exp(0);
      var operatorComparisonExpRight = ctx.exp(1);
      
      if(operatorComparison != null)
        AddSemanticToken(operatorComparison.Start.StartIndex, operatorComparison.Stop.StopIndex, SemanticTokenTypes.@operator);

      if(operatorComparisonExpLeft != null)
        Visit(operatorComparisonExpLeft);
      
      if(operatorComparisonExpRight != null)
        Visit(operatorComparisonExpRight);
      
      return null;
    }
    
    public override object VisitFor(bhlParser.ForContext ctx)
    {
      var forExp = ctx.forExp();
      var forBlock = ctx.block();

      if(forExp != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, forExp.Start.StartIndex-1, SemanticTokenTypes.keyword);
        
        var forStmts = forExp.forPre()?.forStmts()?.forStmt();
        var forCondExp = forExp.forCond()?.exp();
        var forPostIterStmts = forExp.forPostIter()?.forStmts()?.forStmt();

        if(forStmts != null)
        {
          foreach(var forStmt in forStmts)
          {
            var varsDeclareOrCallExps = forStmt.varsDeclareOrCallExps();
            if(varsDeclareOrCallExps != null)
            {
              var varDeclareOrCallExp = varsDeclareOrCallExps.varDeclareOrCallExp();
              if(varDeclareOrCallExp != null)
              {
                foreach(var varDeclareOrCallExpItem in varDeclareOrCallExp)
                {
                  var varDeclare = varDeclareOrCallExpItem.varDeclare();
                  var callExp = varDeclareOrCallExpItem.callExp();

                  if(varDeclare != null)
                    Visit(varDeclare);
          
                  if(callExp != null)
                    Visit(callExp);
                }
              }
              
              var forStmtAssignExp = forStmt.assignExp().exp();
              if(forStmtAssignExp != null)
                Visit(forStmtAssignExp);
            }
            else
            {
              var callPostOperators = forStmt.callPostIncDec();
              if(callPostOperators != null)
                CommonPostIncDec(callPostOperators);
            }
          }
        }

        if(forCondExp != null)
          Visit(forCondExp);
        
        if(forPostIterStmts != null)
        {
          foreach(var forPostIterStmt in forPostIterStmts)
          {
            var varsDeclareOrCallExps = forPostIterStmt.varsDeclareOrCallExps();
            if(varsDeclareOrCallExps != null)
            {
              var varDeclareOrCallExp = varsDeclareOrCallExps.varDeclareOrCallExp();
              if(varDeclareOrCallExp != null)
              {
                foreach(var varDeclareOrCallExpItem in varDeclareOrCallExp)
                {
                  var varDeclare = varDeclareOrCallExpItem.varDeclare();
                  var callExp = varDeclareOrCallExpItem.callExp();

                  if(varDeclare != null)
                    Visit(varDeclare);
          
                  if(callExp != null)
                    Visit(callExp);
                }
              }
              
              var forStmtAssignExp = forPostIterStmt.assignExp().exp();
              if(forStmtAssignExp != null)
                Visit(forStmtAssignExp);
            }
            else
            {
              var callPostOperators = forPostIterStmt.callPostIncDec();
              if(callPostOperators != null)
                CommonPostIncDec(callPostOperators);
            }
          }
        }
      }
      
      if(forBlock != null)
        Visit(forBlock);
      
      return null;
    }
    
    public override object VisitExpAddSub(bhlParser.ExpAddSubContext ctx)
    {
      var operatorAddSub = ctx.operatorAddSub();
      var operatorAddSubExpLeft = ctx.exp(0);
      var operatorAddSubExpRight = ctx.exp(1);
      
      if(operatorAddSub != null)
        AddSemanticToken(operatorAddSub.Start.StartIndex, operatorAddSub.Stop.StopIndex, SemanticTokenTypes.@operator);

      if(operatorAddSubExpLeft != null)
        Visit(operatorAddSubExpLeft);
      
      if(operatorAddSubExpRight != null)
        Visit(operatorAddSubExpRight);
      
      return null;
    }
    
    public override object VisitWhile(bhlParser.WhileContext ctx)
    {
      var whileExp = ctx.exp();
      var whileBlock = ctx.block();

      if(whileExp != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, whileExp.Start.StartIndex-2, SemanticTokenTypes.keyword);
        Visit(whileExp);
      }

      if(whileBlock != null)
        Visit(whileBlock);
      
      return null;
    }
    
    public override object VisitDeclAssign(bhlParser.DeclAssignContext ctx)
    {
      var varDeclareOrCallExp = ctx.varsDeclareAssign().varsDeclareOrCallExps()?.varDeclareOrCallExp();
      var assignExp = ctx.varsDeclareAssign().assignExp()?.exp();

      if(varDeclareOrCallExp != null)
      {
        foreach(var varDeclareOrCallExpItem in varDeclareOrCallExp)
        {
          var varDeclare = varDeclareOrCallExpItem.varDeclare();
          var callExp = varDeclareOrCallExpItem.callExp();

          if(varDeclare != null)
            Visit(varDeclare);
          
          if(callExp != null)
            Visit(callExp);
        }
      }
      
      if(assignExp != null)
        Visit(assignExp);
      
      return null;
    }
    
    public override object VisitSymbCall(bhlParser.SymbCallContext ctx)
    {
      Visit(ctx.callExp());
      return null;
    }
    
    public override object VisitExpCall(bhlParser.ExpCallContext ctx)
    {
      Visit(ctx.callExp());
      return null;
    }
    
    public override object VisitIf(bhlParser.IfContext ctx)
    {
      var mainIf = ctx.mainIf();
      var elseIf = ctx.elseIf();
      var @else = ctx.@else();

      if(mainIf != null)
      {
        var mainIfExp = mainIf.exp();
        var mainIfBlock = mainIf.block();
        
        if(mainIfExp != null)
        {
          AddSemanticToken(mainIf.Start.StartIndex, mainIfExp.Start.StartIndex-2, SemanticTokenTypes.keyword);
          Visit(mainIfExp);
        }

        if(mainIfBlock != null)
          Visit(mainIfBlock);
      }

      if(elseIf != null)
      {
        foreach(var elseIfItem in elseIf)
        {
          var elseIfItemExp = elseIfItem.exp();
          AddSemanticToken(elseIfItem.Start.StartIndex, elseIfItemExp.Start.StartIndex-2, SemanticTokenTypes.keyword);
          Visit(elseIfItemExp);

          var elseIfItemBlock = elseIfItem.block();
          if(elseIfItemBlock != null)
            Visit(elseIfItemBlock);
        }
      }

      if(@else != null)
      {
        var elseBlock = @else.block();
        if(elseBlock != null)
        {
          AddSemanticToken(@else.Start.StartIndex, elseBlock.Start.StartIndex - 1, SemanticTokenTypes.keyword);
          Visit(elseBlock);
        }
      }
      
      return null;
    }
    
    public override object VisitVarDecl(bhlParser.VarDeclContext ctx)
    {
      var varDeclare = ctx.varDeclare();
      VisitVarDeclare(varDeclare);
      return null;
    }

    public override object VisitVarDeclare(bhlParser.VarDeclareContext ctx)
    {
      var varDeclareType = ctx.type();
      var varDeclareName = ctx.NAME();
        
      if(varDeclareType != null)
        Visit(varDeclareType);
        
      if(varDeclareName != null)
        AddSemanticToken(varDeclareName, SemanticTokenTypes.variable);
      
      return null;
    }
    
    public override object VisitFuncParamDeclare(bhlParser.FuncParamDeclareContext ctx)
    {
      var isRef = ctx.isRef();
      var type = ctx.type();
      var name = ctx.NAME();
      var assignExp = ctx.assignExp();
      
      if(isRef != null)
      {
        var refStopIdx = type?.Start.StartIndex ?? (name?.Symbol.StartIndex ?? 0);
        AddSemanticToken(ctx.Start.StartIndex, refStopIdx - 1, SemanticTokenTypes.keyword);
      }

      if(type != null)
        Visit(type);

      AddSemanticToken(name, SemanticTokenTypes.parameter);
      
      if(assignExp != null)
        Visit(assignExp.exp());
      
      return null;
    }
    
    public override object VisitExpLiteralNull(bhlParser.ExpLiteralNullContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.keyword);
      return null;
    }
    
    public override object VisitExpLiteralFalse(bhlParser.ExpLiteralFalseContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.keyword);
      return null;
    }
    
    public override object VisitExpLiteralTrue(bhlParser.ExpLiteralTrueContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.keyword);
      return null;
    }
    
    public override object VisitExpLiteralNum(bhlParser.ExpLiteralNumContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.number);
      return null;
    }
    
    public override object VisitExpUnary(bhlParser.ExpUnaryContext ctx)
    {
      var operatorUnary = ctx.operatorUnary();
      AddSemanticToken(operatorUnary.Start.StartIndex, operatorUnary.Stop.StopIndex, SemanticTokenTypes.@operator);
      Visit(ctx.exp());
      return null;
    }
    
    public override object VisitExpLiteralStr(bhlParser.ExpLiteralStrContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.@string);
      return null;
    }
    
    public override object VisitExpJsonObj(bhlParser.ExpJsonObjContext ctx)
    {
      Visit(ctx.jsonObject());
      return null;
    }
    
    public override object VisitJsonObject(bhlParser.JsonObjectContext ctx)
    {
      var newExp = ctx.newExp();
      if(newExp != null)
        VisitNewExp(newExp);

      var jsonPair = ctx.jsonPair();
      if(jsonPair != null)
      {
        foreach(var jsonPairItem in jsonPair)
        {
          var jsonPairItemName = jsonPairItem.NAME();
          var jsonValue = jsonPairItem.jsonValue();
          
          if(jsonPairItemName != null)
            AddSemanticToken(jsonPairItemName, SemanticTokenTypes.variable);
          
          if(jsonValue != null)
            Visit(jsonValue.exp());
        }
      }
      
      return null;
    }
    
    public override object VisitNewExp(bhlParser.NewExpContext ctx)
    {
      var newExpType = ctx.type();
        
      AddSemanticToken(ctx.Start.StartIndex, newExpType.Start.StartIndex - 1, SemanticTokenTypes.keyword);
      Visit(newExpType);
      
      return null;
    }
    
    public override object VisitCallExp(bhlParser.CallExpContext ctx)
    {
      var name = ctx.NAME();
      var chainExp = ctx.chainExp();
      
      if(chainExp != null)
      {
        foreach(var chainExpItem in chainExp)
        {
          if(chainExpItem.callArgs() is bhlParser.CallArgsContext callArgs)
          {
            if(name != null)
            {
              AddSemanticToken(name, SemanticTokenTypes.function);
              name = null;
            }

            Visit(callArgs);
          }
          else if(chainExpItem.memberAccess() is bhlParser.MemberAccessContext memberAccess)
          {
            if(name != null)
              AddSemanticToken(name, SemanticTokenTypes.variable);
            
            name = memberAccess.NAME();

            Visit(memberAccess);
          }
          else if(chainExpItem.arrAccess() is bhlParser.ArrAccessContext arrAccess)
          {
            if(name != null)
              AddSemanticToken(name, SemanticTokenTypes.variable);
            
            name = null;

            Visit(arrAccess);
          }
        }
        
        if(name != null)
          AddSemanticToken(name, SemanticTokenTypes.variable);
      }
      return null;
    }

    public override object VisitArrAccess(bhlParser.ArrAccessContext ctx)
    {
      return null;
    }
    
    public override object VisitMemberAccess(bhlParser.MemberAccessContext ctx)
    {
      return null;
    }
    
    public override object VisitCallArgs(bhlParser.CallArgsContext ctx)
    {
      foreach(var callArg in ctx.callArg())
      {
        var callArgName = callArg.NAME();
        if(callArgName != null)
          AddSemanticToken(callArgName, SemanticTokenTypes.parameter);
              
        var callArgIsRef = callArg.isRef();
        if(callArgIsRef != null)
          AddSemanticToken(callArgIsRef.Start.StartIndex, callArgIsRef.Stop.StopIndex, SemanticTokenTypes.keyword);

        var callArgExp = callArg.exp();
        if(callArgExp != null)
          Visit(callArg.exp());
      }
      return null;
    }
    
    public override object VisitVarDeclareAssign(bhlParser.VarDeclareAssignContext ctx)
    {
      var varDeclare = ctx.varDeclare();
      var assignExp = ctx.assignExp();
      
      if(varDeclare != null)
        Visit(varDeclare);

      if(assignExp != null)
        Visit(assignExp.exp());
      
      return null;
    }
    
    public override object VisitImports(bhlParser.ImportsContext ctx)
    {
      foreach(var mimport in ctx.mimport())
      {
        var normalstring = mimport.NORMALSTRING();
        
        var import = normalstring.GetText();
        import = import.Substring(1, import.Length-2); // removing quotes
        imports.Add(import);
        
        AddSemanticToken(mimport.Start.StartIndex, normalstring.Symbol.StartIndex - 1, SemanticTokenTypes.keyword);
        AddSemanticToken(normalstring, SemanticTokenTypes.@string);
      }
      
      return null;
    }

    public override object VisitExpTypeof(bhlParser.ExpTypeofContext ctx)
    {
      /*var typeIdType = ctx.typeid()?.type();
      if(typeIdType != null)
      {
        AddSemanticToken(ctx.Start.StartIndex, typeIdType.Start.StartIndex-2, SemanticTokenTypes.keyword);
        Visit(typeIdType);
      }*/
      
      return null;
    }
    
    public override object VisitType(bhlParser.TypeContext ctx)
    {
      //TODO: parse the whole nsName()
      AddSemanticTokenTypeName(ctx.nsName()?.dotName().NAME());

      var fnType = ctx.funcType();
      if(fnType != null && fnType.types() is bhlParser.TypesContext types)
      {
        foreach(var refType in types.refType())
        {
          var refNameIsRef = refType.isRef();
          //TODO: parse the whole nsName()
          var refNameName = refType.type()?.nsName()?.dotName().NAME();
          if(refNameName != null)
          {
            if(refNameIsRef != null)
              AddSemanticToken(refNameIsRef.Start.StartIndex, refNameName.Symbol.StartIndex-1, SemanticTokenTypes.keyword);
          
            AddSemanticTokenTypeName(refNameName);
          }
        }
      }
      return null;
    }
    
    public override object VisitExpTernaryIf(bhlParser.ExpTernaryIfContext ctx)
    {
      var ternaryIf = ctx.ternaryIfExp();
      if(ternaryIf != null)
      {
        var ternaryIfExp = ctx.exp();
        var ternaryIfExpLeft = ternaryIf.exp(0);
        var ternaryIfExpRight = ternaryIf.exp(1);

        if(ternaryIfExp != null)
          Visit(ternaryIfExp);
        
        if(ternaryIfExpLeft != null)
          Visit(ternaryIfExpLeft);
        
        if(ternaryIfExpRight != null)
          Visit(ternaryIfExpRight);
      }
      
      return null;
    }
    
    public override object VisitVarPostIncDec(bhlParser.VarPostIncDecContext ctx)
    {
      CommonPostIncDec(ctx.callPostIncDec());
      return null;
    }

    void CommonPostIncDec(bhlParser.CallPostIncDecContext ctx)
    {
      ////TODO: take into account the whole name
      //var callPostOperatorName = ctx.dotName().NAME();
      //if(callPostOperatorName != null)
      //  AddSemanticToken(callPostOperatorName, SemanticTokenTypes.variable);
      //
      //var decrementOperator = ctx.decrementOperator();
      //var incrementOperator = ctx.incrementOperator();
      //
      //if(decrementOperator != null)
      //  AddSemanticToken(decrementOperator.Start.StartIndex, decrementOperator.Stop.StopIndex, SemanticTokenTypes.@operator);
      //
      //if(incrementOperator != null)
      //  AddSemanticToken(incrementOperator.Start.StartIndex, incrementOperator.Stop.StopIndex, SemanticTokenTypes.@operator);
    }

    public override object VisitContinue(bhlParser.ContinueContext ctx)
    {
      AddSemanticToken(ctx.Start.StartIndex, ctx.Stop.StopIndex, SemanticTokenTypes.keyword);
      return null;
    }
    
    bool IsTypeKeyword(string typeName)
    {
      return Types.Int.name    == typeName ||
             Types.Float.name  == typeName ||
             Types.String.name == typeName ||
             Types.Bool.name   == typeName ||
             Types.Any.name    == typeName ||
             Types.Null.name   == typeName ||
             Types.Void.name   == typeName;
    }

    private void AddSemanticTokenTypeName(ITerminalNode node)
    {
      if(node == null)
        return;
      
      if(IsTypeKeyword(node.GetText()))
        AddSemanticToken(node, SemanticTokenTypes.keyword);
      else
        AddSemanticToken(node, SemanticTokenTypes.type);
    }
    
    private void AddSemanticToken(ITerminalNode node, string tokenType, params string[] tokenModifiers)
    {
      if(node == null)
        return;
      
      AddSemanticToken(node.Symbol.StartIndex, node.Symbol.StopIndex, tokenType, tokenModifiers);
    }

    private void AddSemanticToken(int startIdx, int stopIdx, string tokenType, params string[] tokenModifiers)
    {
      if(startIdx < 0 || stopIdx < 0)
        return;
      
      if(string.IsNullOrEmpty(tokenType))
        return;
    
      var t = Array.IndexOf(BHLSemanticTokens.semanticTokenTypes, tokenType);
      if(t < 0)
        return;
      
      var nextStart = document.GetLineColumn(next);
      var lineColumnSymbol = document.GetLineColumn(startIdx);

      var diffLine = lineColumnSymbol.Item1 - nextStart.Item1;
      var diffColumn = diffLine != 0 ? lineColumnSymbol.Item2 : lineColumnSymbol.Item2 - nextStart.Item2;

      int bitTokenModifiers = 0;
      for(int i = 0; i < tokenModifiers.Length; i++)
      {
        var idx = Array.IndexOf(BHLSemanticTokens.semanticTokenModifiers, tokenModifiers[i]);
        bitTokenModifiers |= (int)Math.Pow(2, idx);
      }
      
      // line
      dataSemanticTokens.Add((uint)diffLine);
      // startChar
      dataSemanticTokens.Add((uint)diffColumn);
      // length
      dataSemanticTokens.Add((uint)(stopIdx - startIdx + 1));
      // tokenType
      dataSemanticTokens.Add((uint)t);
      // tokenModifiers
      dataSemanticTokens.Add((uint)bitTokenModifiers);

      next = startIdx;
    }
  }

  public static class BHLSemanticTokens
  {
    public static string[] semanticTokenTypes = 
    {
        SemanticTokenTypes.@class,
        SemanticTokenTypes.function,
        SemanticTokenTypes.variable,
        SemanticTokenTypes.number,
        SemanticTokenTypes.@string,
        SemanticTokenTypes.type,
        SemanticTokenTypes.keyword
    };
    
    public static string[] semanticTokenModifiers = 
    {
        SemanticTokenModifiers.declaration,   // 1
        SemanticTokenModifiers.definition,    // 2
        SemanticTokenModifiers.@readonly,     // 4
        SemanticTokenModifiers.@static,       // 8
        SemanticTokenModifiers.deprecated,    // 16
        SemanticTokenModifiers.@abstract,     // 32
        SemanticTokenModifiers.async,         // 64
        SemanticTokenModifiers.modification,  // 128
        SemanticTokenModifiers.documentation, // 256
        SemanticTokenModifiers.defaultLibrary // 512
    };
  }
  
  public class BHLTextDocument : BHLSPTextDocument
  {
    private readonly BHLTextDocumentVisitor visitor = new BHLTextDocumentVisitor();
    
    public List<string> Imports => visitor.imports;
    public Dictionary<string, bhlParser.ClassDeclContext> ClassDecls => visitor.classDecls;
    public Dictionary<string, bhlParser.FuncDeclContext> FuncDecls => visitor.funcDecls;
    public List<uint> DataSemanticTokens => visitor.dataSemanticTokens;
    
    public override void Sync(string text)
    {
      base.Sync(text);
      visitor.VisitDocument(this);
    }
    
    public bhlParser ToParser()
    {
      var ais = new AntlrInputStream(text.ToStream());
      var lex = new bhlLexer(ais);
      var tokens = new CommonTokenStream(lex);
      var parser = new bhlParser(tokens);
      
      lex.RemoveErrorListeners();
      parser.RemoveErrorListeners();

      return parser;
    }
  }
}
