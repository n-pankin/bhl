using System;
using System.IO;

using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Antlr4.Runtime.Dfa;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Sharpen;

namespace bhl {

public interface ICompileError
{
  string text { get; }
  string stack_trace { get; }
  string file { get; }
  int line { get; }
  int char_pos { get; }
}

public class SyntaxError : Exception, ICompileError
{
  public string text { get; }
  public string stack_trace { get { return StackTrace; } }
  public int line { get; }
  public int char_pos { get; }
  public string file { get; }

  public SyntaxError(string file, int line, int char_pos, string msg)
    : base(ErrorUtils.MakeMessage(file, line, char_pos, msg))
  {
    this.text = msg;
    this.line = line;
    this.char_pos = char_pos;
    this.file = file;
  }
}

public class BuildError : Exception, ICompileError
{
  public string text { get; }
  public string stack_trace { get { return StackTrace; } }
  public int line { get { return 0; } }
  public int char_pos { get { return 0; } }
  public string file { get; }

  public BuildError(string file, string msg)
    : base(ErrorUtils.MakeMessage(file, 0, 0, msg))
  {
    this.text = msg;
    this.file = file;
  }

  public BuildError(string file, Exception inner)
    : base(ErrorUtils.MakeMessage(file, 0, 0, inner.Message), inner)
  {
    this.text = inner.Message;
    this.file = file;
  }
}

public class SemanticError : Exception, ICompileError
{
  public string text { get; }
  public string stack_trace { get { return StackTrace; } }
  public int line { get { return tokens.Get(place.SourceInterval.a).Line; } }
  public int char_pos { get { return tokens.Get(place.SourceInterval.a).Column; } }
  public string file { get { return module.file_path; } }

  public Module module { get; }
  public IParseTree place { get; }
  public ITokenStream tokens { get; }

  public SemanticError(Module module, IParseTree place, ITokenStream tokens, string msg)
    : base(ErrorUtils.MakeMessage(module, place, tokens, msg))
  {
    this.text = msg;
    this.module = module;
    this.place = place;
    this.tokens = tokens;
  }

  public SemanticError(WrappedParseTree w, string msg)
    : this(w.module, w.tree, w.tokens, msg)
  {}
}

public class ErrorLexerListener : IAntlrErrorListener<int>
{
  string file_path;

  public ErrorLexerListener(string file_path)
  {
    this.file_path = file_path;
  }

  public virtual void SyntaxError(TextWriter tw, IRecognizer recognizer, int offendingSymbol, int line, int char_pos, string msg, RecognitionException e)
  {
    throw new SyntaxError(file_path, line, char_pos, msg);
  }
}

public class ErrorStrategy : DefaultErrorStrategy
{
  public override void Sync(Antlr4.Runtime.Parser recognizer) {}
}

public class ErrorParserListener : IParserErrorListener
{
  string file_path;

  public ErrorParserListener(string file_path)
  {
    this.file_path = file_path;
  }

  public virtual void SyntaxError(TextWriter tw, IRecognizer recognizer, IToken offendingSymbol, int line, int char_pos, string msg, RecognitionException e)
  {
    throw new SyntaxError(file_path, line, char_pos, msg);
  }

  public virtual void ReportAmbiguity(Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex, bool exact, BitSet ambigAlts, ATNConfigSet configs)
  {}
  public virtual void ReportAttemptingFullContext(Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex, BitSet conflictingAlts, SimulatorState conflictState)
  {}
  public virtual void ReportContextSensitivity(Antlr4.Runtime.Parser recognizer, DFA dfa, int startIndex, int stopIndex, int prediction, SimulatorState acceptState)
  {}
}

} //namespace bhl
