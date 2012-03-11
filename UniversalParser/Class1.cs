using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;

namespace UniversalParser
{
    #region GrammarNode

    public interface IMatchable
    {
        bool Match(Token ToMatch);
    }

    public enum GrammarNodeType
    {
        ALTERNATION,
        SEQUENCE,
        TERMINAL
    }

    public enum GrammarNodeTypeQuantifier
    {
        ONE,
        ZERO_OR_ONE,
        ZERO_OR_MORE,
        ONE_OR_MORE
    }

    public enum MatchType
    {
        TYPE,
        VALUE,
        EOF,
        RECURSE, // Placeholder value for code readability
        REGEX // applied to value
    }

    [System.Serializable]
    public class GrammarNode : List<GrammarNode>, IMatchable
    {
        public GrammarNodeType GNType;
        public GrammarNodeTypeQuantifier GNTQuantifier;
        public string MatchText;
        public MatchType MatchOn;
        public GrammarNode(GrammarNodeType GNType, string MatchText, MatchType MType)
            : this(GNType, MatchText, MType, GrammarNodeTypeQuantifier.ONE)
        { }

        public GrammarNode(GrammarNodeType GNType, string MatchText, MatchType MType, GrammarNodeTypeQuantifier Quantity)
        {
            this.GNType = GNType;
            this.MatchText = MatchText;
            this.MatchOn = MType;
            this.GNTQuantifier = Quantity;
        }



        #region IMatchable Members

        public bool Match(Token ToMatch)
        {
            switch (MatchOn)
            {
                case MatchType.TYPE:
                    return MatchText == ToMatch.TokenType;
                case MatchType.VALUE:
                    return MatchText == ToMatch.Value;
                case MatchType.REGEX:
                    Regex charclass = new Regex(MatchText);
                    return charclass.Match(ToMatch.Value).Success;
                case MatchType.EOF:
                    return ToMatch.IsEOF;
                default:
                    throw new Exception("Unrecognized match type");
            }
        }

        #endregion
    }
    #endregion




    public class Token
    {
        public String TokenType;
        public String Value;
        public bool IsEOF;
        public Token(String TType, String Value)
        {
            this.TokenType = TType;
            this.Value = Value;
            IsEOF = false;
        }
        public Token()
            : this("EOF", "EOF")
        {
            this.IsEOF = true;
        }
    }


    public class TokenList : List<Token>
    {
        public String tag;
        public TokenList(String tag, List<Token> list)
            : base(list)
        {
            this.tag = tag;
        }
    }

    public class Validator
    {
        private ITokenStream Source;
        public Dictionary<String, GrammarNode> Productions;

        public Validator(ITokenStream Source, Dictionary<String, GrammarNode> Productions)
        {
            this.Source = Source;
            this.Productions = Productions;
            this.SentEOF = false;
        }

        public bool EOF
        {
            get
            {
                return Source.EOF;
            }
        }

        private bool SentEOF;

        public TokenList GetProduction()
        {
            int StartPosition = Source.Position;

            foreach (KeyValuePair<String, GrammarNode> kvp in Productions)
            {
                if (AdvanceQuantified(kvp.Value))
                {
                    return new TokenList(kvp.Key, Source.Range(StartPosition, Source.Position - StartPosition));
                }
                Source.Position = StartPosition;
            }
            throw new Exception("No valid production found.");

        }

        public bool AdvanceQuantified(GrammarNode Rule)
        {
            //Console.WriteLine("Trying to find: " + Rule.GNTQuantifier.ToString() + " of " + Rule.MatchText + " by matching on " + Rule.MatchOn.ToString());
            switch (Rule.GNTQuantifier)
            {
                case GrammarNodeTypeQuantifier.ONE:
                    return Advance(Rule);

                case GrammarNodeTypeQuantifier.ZERO_OR_ONE:
                    Advance(Rule);
                    return true;

                case GrammarNodeTypeQuantifier.ZERO_OR_MORE:
                    while (Advance(Rule))
                    {
                        // NO-OP
                    }
                    return true;

                case GrammarNodeTypeQuantifier.ONE_OR_MORE:
                    if (Advance(Rule))
                    {
                        while (Advance(Rule))
                        {
                            // NO-OP
                        }
                        return true;
                    }
                    return false;

                default:
                    throw new Exception("Unrecognized GrammarNodeTypeQuantifier");
            }
        }

        public bool Advance(GrammarNode Rule)
        {
            int InitialPosition = Source.Position;
            switch (Rule.GNType)
            {
                case GrammarNodeType.ALTERNATION:
                    foreach (GrammarNode gn in Rule)
                    {
                        if (AdvanceQuantified(gn))
                        {
                            return true;
                        }
                    }
                    Source.Position = InitialPosition;
                    return false;

                case GrammarNodeType.SEQUENCE:
                    foreach (GrammarNode gn in Rule)
                    {
                        if (!AdvanceQuantified(gn))
                        {
                            Source.Position = InitialPosition;
                            return false;
                        }
                    }
                    return true;

                case GrammarNodeType.TERMINAL:
                    if (Rule.Match(Source.ReadToken()))
                    {
                        return true;
                    }
                    Source.Position = InitialPosition;
                    return false;

                default:
                    throw new Exception("Unrecognized GrammarNodeType");
            }
        }
    }

    public interface ITokenStream
    {
        Token ReadToken();
        int Position
        {
            get;
            set;
        }
        bool EOF
        {
            get;
        }
        List<Token> Range(int Start, int Count);
        Token this[int i]
        {
            get;
        }
    }

    // Hands off one token for each character.
    public class CodeStream : ITokenStream
    {
        private List<Token> built;
        private String Code;
        private int pos;
        bool eof;
        public CodeStream(String Code)
        {
            this.Code = Code;
            this.pos = 0;
            built = new List<Token>();
        }
        #region ITokenStream Members

        public Token ReadToken()
        {
            // Fill in up to the token to hand off, *one **self-named** token per character*.
            return this[pos++];
        }

        public int Position
        {
            get
            {
                return pos;
            }
            set
            {
                pos = value;
            }
        }

        public bool EOF
        {
            get { return eof; }
        }
        // For both of these, build the tokens up to the requested end point, then give the user what they ask for or barf if hit eof
        public List<Token> Range(int Start, int Count)
        {
            // EOF token makes this.Count 1 longer than Code.Length
            int End = Math.Min(Start + Count, Code.Length + 1);

            List<Token> t = new List<Token>();
            for (int i = Start; i < End; i++)
            {
                t.Add(this[i]);
            }
            return t; ;
        }

        public Token this[int i]
        {
            get
            {
                for (int j = Math.Min(built.Count, Code.Length); j <= i; j++)
                {
                    if (j >= Code.Length && !eof)
                    {
                        built.Add(new Token()); // EOF token
                        this.eof = true;
                        break;
                    }
                    else if (j >= Code.Length) // Already put EOF token on.
                    {
                        break;
                    }
                    else // j < Code.Length, so add non-EOF token for character
                    {
                        built.Add(new Token(Code[j].ToString(), Code[j].ToString()));
                    }
                }
                return built[i];
            }
        }

        #endregion
    }

    public class Scanner : ITokenStream
    {
        private Validator CodeValidator; //Generates tagged lists of character tokens
        private List<Token> scanned;
        private int TokenPosition;
        private bool eof;
        private Dictionary<String, GrammarNode> Ignore;

        public Scanner(ITokenStream Code, Dictionary<String, GrammarNode> Productions, Dictionary<string, GrammarNode> Ignore)
        {
            CodeValidator = new Validator(Code, Productions);
            scanned = new List<Token>();
            TokenPosition = 0;
            eof = false;
            this.Ignore = Ignore;
        }

        // Concatenate the valid token list--chars, really--into a token that the parser will understand.
        private Token Compile(TokenList taggedChars)
        {
            StringBuilder lexeme = new StringBuilder();
            foreach (Token t in taggedChars)
            {
                if (t.IsEOF)
                {
                    return t;
                }
                else
                {
                    lexeme.Append(t.Value);
                }
            }
            return new Token(taggedChars.tag, lexeme.ToString());
        }

        #region ITokenStream Members

        // Don't ignore EOF and you should be fine.
        public Token ReadToken()
        {
            while (Ignore.ContainsKey(this[TokenPosition].TokenType))
            {
                TokenPosition++;
            }
            return this[TokenPosition++];
        }

        // NOTE to maintainers: Range() uses the index for the side effect that it fills up scanned
        //                      to the value of index as best it can.
        public Token this[int i]
        {
            get
            {
                for (int j = scanned.Count; j <= i; j++)
                {
                    if (j > 0 && scanned[j - 1].IsEOF)
                    {
                        return scanned[j - 1]; // We've fallen off the end. Just do the right thing.
                    }
                    else
                    {
                        scanned.Add(Compile(CodeValidator.GetProduction()));
                    }
                }
                if (scanned[i].IsEOF)
                {
                    eof = true;
                }
                return scanned[i]; // We know this exists, here.
            }
        }

        // This is the position in the produced token list.
        // We keep a token buffer and only add to it when we have to.
        public int Position
        {
            get
            {
                return TokenPosition;
            }
            set
            {
                TokenPosition = value;
                if (TokenPosition >= scanned.Count - 1 && scanned[scanned.Count - 1].IsEOF)
                {
                    eof = true;
                }
                else
                {
                    eof = false;
                }
            }
        }

        public bool EOF
        {
            get { return eof; }
        }

        public List<Token> Range(int Start, int Count)
        {
            Token trash = this[Start + Count]; // Force filling of scanned up to Start + Count
            int End = Math.Min(Start + Count, scanned.Count);

            List<Token> t = new List<Token>();
            for (int i = Start; i < End; i++)
            {
                t.Add(scanned[i]);
            }
            return t;
        }

        #endregion // ITokenStream members
    }

    public interface IParser
    {
        ICompiler Target
        {
            get;
            set;
        }
        void Parse();

        Validator Checker
        {
            get;
            set;
        }
    }

    public class Parser : IParser
    {
        public Parser(ICompiler Target, Scanner SourceScanner, Dictionary<string, GrammarNode> LanguageProductions)
        {
            target = (DiagnosticCompiler)Target;
            this.Checker = new Validator(SourceScanner, LanguageProductions);
        }

        private DiagnosticCompiler target;
        public ICompiler Target
        {
            get
            {
                return target;
            }
            set
            {
                target = (DiagnosticCompiler)value;
            }
        }

        private Validator checker;

        public Validator Checker
        {
            get
            {
                return checker;
            }
            set
            {
                checker = value;
            }
        }

        public void Parse()
        {
            while (true)
            {
                Target.Build(Checker.GetProduction());
                if (Checker.EOF)
                {
                    break;
                }
            }
        }

    }


    public interface ICompiler
    {
        void Build(TokenList Unit);
        String OutputFile
        {
            get;
            set;
        }
    }


    public class DiagnosticCompiler : ICompiler
    {
        public DiagnosticCompiler(String outputFile)
        {
            this.outputpath = outputFile;
            this.ofile = new StreamWriter(this.outputpath);
        }
        public void Build(TokenList Unit)
        {
            if (Unit.tag != "whitespace")
            {
                ofile.WriteLine("BeginUnit: " + Unit.tag);
                foreach (Token t in Unit)
                {
                    if (t.TokenType != "whitespace")
                    {
                        ofile.WriteLine(t.TokenType + ": " + t.Value);
                    }
                }
                ofile.WriteLine("EndUnit: " + Unit.tag);
                ofile.Flush();
            }
        }
        private StreamWriter ofile;
        private String outputpath;
        public String OutputFile
        {
            get
            {
                return outputpath;
            }
            set
            {
                outputpath = value;
                if (ofile != null)
                {
                    ofile.Flush();
                    ofile.Close();
                }
                ofile = new StreamWriter(outputpath);
            }
        }

    }
    [Serializable]
    public class TestLanguage
    {
        public Dictionary<string, GrammarNode> ScannerProductions;
        public Dictionary<string, GrammarNode> ParserProductions;
        public Dictionary<String, GrammarNode> Ignore;

        public TestLanguage()
        {
            // The scanner will understand tokens in the sentence "(one foo baz   )", for example.
            // Incidentally, it will also understand them in "())( zab (((()", but that's not the scanner's problem.
            ScannerProductions = new Dictionary<string, GrammarNode>();

            GrammarNode SPName = new GrammarNode(GrammarNodeType.TERMINAL, "[a-z]", MatchType.REGEX);
            SPName.GNTQuantifier = GrammarNodeTypeQuantifier.ONE_OR_MORE;
            ScannerProductions.Add("name", SPName);

            GrammarNode SPLParen = new GrammarNode(GrammarNodeType.TERMINAL, "(", MatchType.VALUE);
            SPLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            ScannerProductions.Add("lparen", SPLParen);

            GrammarNode SPRParen = new GrammarNode(GrammarNodeType.TERMINAL, ")", MatchType.VALUE);
            SPLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            ScannerProductions.Add("rparen", SPRParen);

            GrammarNode SPWhiteSpace = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);
            SPWhiteSpace.GNTQuantifier = GrammarNodeTypeQuantifier.ONE_OR_MORE;
            GrammarNode SPSpace = new GrammarNode(GrammarNodeType.TERMINAL, " ", MatchType.VALUE);
            SPSpace.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            GrammarNode SPReturn = new GrammarNode(GrammarNodeType.TERMINAL, "\r", MatchType.REGEX);
            SPReturn.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            GrammarNode SPNewline = new GrammarNode(GrammarNodeType.TERMINAL, "\n", MatchType.REGEX);
            SPNewline.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            SPWhiteSpace.Add(SPSpace);
            SPWhiteSpace.Add(SPReturn);
            SPWhiteSpace.Add(SPNewline);
            ScannerProductions.Add("whitespace", SPWhiteSpace);

            GrammarNode SPEOF = new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.TYPE);
            SPEOF.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            ScannerProductions.Add("EOF", SPEOF);


            // The parser will understand sentences of the form "(foo baz bar   one   )", for example.
            // It will also understand "(foo (baz bar)   one )". That is, recursive lists of simple alphabetic string atoms.

            ParserProductions = new Dictionary<string, GrammarNode>();

            //GrammarNode PWhitespace = new GrammarNode(GrammarNodeType.TERMINAL, "whitespace", MatchType.TYPE);
            //PWhitespace.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            //GrammarNode PWSZeroOrOne = new GrammarNode(GrammarNodeType.TERMINAL, "whitespace", MatchType.TYPE);
            //PWSZeroOrOne.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_ONE;

            GrammarNode PLBegin = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            PLBegin.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;

            GrammarNode PLParen = new GrammarNode(GrammarNodeType.TERMINAL, "lparen", MatchType.TYPE);
            PLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;

            PLBegin.Add(PLParen);
            //PLBegin.Add(PWSZeroOrOne);

            GrammarNode PName = new GrammarNode(GrammarNodeType.TERMINAL, "name", MatchType.TYPE);
            PName.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;

            GrammarNode PList = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            PList.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;

            GrammarNode PListElement = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);
            PListElement.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_MORE;

            PListElement.Add(PName);
            PListElement.Add(PList);

            //GrammarNode PConsequent = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            //PConsequent.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_MORE;

            //PConsequent.Add(PWhitespace);
            //PConsequent.Add(PInitial);

            GrammarNode PLEnd = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            PLEnd.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;

            GrammarNode PRParen = new GrammarNode(GrammarNodeType.TERMINAL, "rparen", MatchType.TYPE);
            PRParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;

            //PLEnd.Add(PWSZeroOrOne);
            PLEnd.Add(PRParen);



            PList.Add(PLBegin);
            PList.Add(PListElement);
            //PList.Add(PConsequent);
            PList.Add(PLEnd);

            GrammarNode EOF = new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.EOF);

            //ParserProductions.Add("whitespace", PWhitespace);
            ParserProductions.Add("list", PList);
            ParserProductions.Add("EOF", EOF);

            // The scanner will suppress whitespace, which will simplify parsing.
            Ignore = new Dictionary<string, GrammarNode>();

            Ignore.Add("whitespace", SPWhiteSpace); // It's only the key that matters.


        }
    }
}
