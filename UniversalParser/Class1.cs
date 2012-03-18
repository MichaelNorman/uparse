using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Formatters.Binary;

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
                    // Regex must match on one character. Really useful only for character classes.
                    Regex charclass = new Regex(MatchText);
                    return charclass.Match(ToMatch.Value).Success;
                case MatchType.EOF:
                    return ToMatch.IsEOF;
                default:
                    throw new Exception("Unrecognized match type");
            }
        }

        #endregion

        #region StaticMethods
        public static GrammarNode ToSequence(string ToSequence, GrammarNodeTypeQuantifier NumTimes)
        {
            if (ToSequence.Length == 1)
            {
                return new GrammarNode(GrammarNodeType.TERMINAL, ToSequence, MatchType.VALUE, NumTimes);
            }

            if (ToSequence.Length == 0)
            {
                throw new Exception("Cannot match zero-length string.");
            }

            GrammarNode ToReturn = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE, NumTimes);
            for (int i = 0; i < ToSequence.Length; i++)
            {
                ToReturn.Add(new GrammarNode(GrammarNodeType.TERMINAL, ToSequence[i].ToString(), MatchType.VALUE));
            }
            return ToReturn;
        }

        public static GrammarNode TerminalNodeByType(string MatchText)
        {
            return new GrammarNode(GrammarNodeType.TERMINAL, MatchText, MatchType.TYPE);
        }

        public static GrammarNode SequenceNode()
        {
            return new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
        }
        #endregion

        public GrammarNode Copy()
        {
            GrammarNode Copy = new GrammarNode(this.GNType, this.MatchText, this.MatchOn);
            Copy.GNTQuantifier = this.GNTQuantifier;
            foreach (GrammarNode gn in this)
            {
                Copy.Add(gn);
            }
            return Copy;
        }
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
        public List<string> Names;
        public Dictionary<String, GrammarNode> Productions;
        public string tag;
        public Validator(ITokenStream Source, List<string> Names, Dictionary<String, GrammarNode> Productions)
        {
            this.Source = Source;
            this.Productions = Productions;
            this.Names = Names;
        }

        public bool EOF
        {
            get
            {
                return Source.EOF;
            }
        }

        public TokenList GetProduction()
        {
            int StartPosition = Source.Position;

            foreach (string name in Names)
            {
                GrammarNode Production = Productions[name];
                Console.WriteLine(tag + " trying to find " + name);
                if (AdvanceQuantified(Production))
                {
                    Console.WriteLine(tag + ":");
                    Console.WriteLine(Production.GNTQuantifier.ToString() + " " + name + " by " + Production.GNType.ToString() + " on " + Production.MatchOn.ToString());
                    return new TokenList(name, Source.Range(StartPosition, Source.Position - StartPosition));
                }
                Source.Position = StartPosition;
            }
            throw new Exception("No valid production found.");

        }

        public bool AdvanceQuantified(GrammarNode Rule)
        {
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
        private Dictionary<String, string> Ignore;
        private string tag;
        public Scanner(ITokenStream Code, List<string> Names, Dictionary<String, GrammarNode> Productions, Dictionary<string, string> Ignore)
        {
            CodeValidator = new Validator(Code, Names, Productions);
            tag = "Scanner";
            CodeValidator.tag = this.tag;
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
                        // read until not ignored. add first not ignored to scanned.
                        Token currentToken = Compile(CodeValidator.GetProduction());
                        while (Ignore.ContainsKey(currentToken.TokenType))
                        {
                            currentToken = Compile(CodeValidator.GetProduction());
                        }
                        scanned.Add(currentToken);
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
        public Parser(ICompiler Target, Scanner SourceScanner, List<string> LanguageNames, Dictionary<string, GrammarNode> LanguageProductions)
        {
            target = (DiagnosticCompiler)Target;
            this.Checker = new Validator(SourceScanner, LanguageNames, LanguageProductions);
            tag = "Parser";
            Checker.tag = this.tag;
            
        }
        private string tag;
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
                    Target.Build(Checker.GetProduction());
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
            //ofile.AutoFlush = true;
        }
        public void Build(TokenList Unit)
        {
            
            //Console.WriteLine("BeginUnit: " + Unit.tag);
            ofile.WriteLine("BeginUnit: " + Unit.tag);
            foreach (Token t in Unit)
            {
                
                //Console.WriteLine(t.TokenType + ": " + t.Value);
                ofile.WriteLine(t.TokenType + ": " + t.Value);
                    
                
            }
            ofile.WriteLine("EndUnit: " + Unit.tag);
            //Console.WriteLine("EndUnit: " + Unit.tag);
            ofile.Flush();
           
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

    public class ParserCompiler : ICompiler
    {
        private Language LanguageToFill;
        private Dictionary<string, GrammarNode> CurrentEnvironment;
        private string CurrentEnvironmentName;
        private Dictionary<string /*environment name*/, Dictionary<string /*production name*/, GrammarNode>> Names;
        
        private Dictionary<string, Dictionary<string, GrammarNode>> SingularNames;
        private Dictionary<string, Dictionary<string, List<GrammarNode>>> Mentions;


        public ParserCompiler()
        {
            LanguageToFill = new Language();
            SingularNames = new Dictionary<string, Dictionary<string, GrammarNode>>();
            SingularNames.Add("scanner", new Dictionary<string, GrammarNode>());
            SingularNames.Add("parser", new Dictionary<string, GrammarNode>());
            Mentions = new Dictionary<string, Dictionary<string, List<GrammarNode>>>();
            Mentions.Add("scanner", new Dictionary<string, List<GrammarNode>>());
            Mentions.Add("parser", new Dictionary<string, List<GrammarNode>>());
            //ScannerNames = new Dictionary<string, bool>();
            //ParserNames = new Dictionary<string, bool>();
            
        }
        #region ICompiler Members

        public void Build(TokenList Unit)
        {
            Token Head = Unit[0];

            if (Head.IsEOF)
            {
                Cleanup();
                return;
            }

            switch (Head.TokenType)
            {
                case "language_specifier":
                    LanguageToFill.LanguageName = Head.Value.Replace("%", "").Replace(" ", "");
                    break;
                case "environment_specifier":
                    EnterEnvironment(Head.Value.Replace("%", "").Replace(" ", ""));
                    break;
                case "production":
                    BuildProduction(Unit);
                    break;
                default:
                    throw new Exception("Unrecognized syntax beginning with: " + Head.TokenType);
            }
        }

        private string ofile;

        public string OutputFile
        {
            get
            {
                return ofile;
            }
            set
            {
               ofile = value;
            }
        }

        #endregion

        private bool InParserEnvironment()
        {
            return CurrentEnvironment == LanguageToFill.ParserProductions;
        }

        private void EnterEnvironment(string CompilerEnvironment)
        {
            switch (CompilerEnvironment)
            {
                case "scanner":
                    CurrentEnvironment = LanguageToFill.ScannerProductions;
                    break;
                case "parser":
                    CurrentEnvironment = LanguageToFill.ParserProductions;
                    break;
                default:
                    throw new Exception("Unrecognized compiler environment: " + CompilerEnvironment);
            }
            CurrentEnvironmentName = CompilerEnvironment;
        }
        
        private void BuildProduction(TokenList Unit)
        {
            RegisterDelayedGrammarNode(Unit[0]);

            GrammarNode Top = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);

            int i = 2; // The third token is the first name.

            while (i < Unit.Count - 1) // Don't try to build the terminator.
            {
                Top.Add(FillSequence(ref i, Unit));
            }
        }
        //****************************************************************************************************************************//
        // Store away an original if one doesn't exist. Store a copy in a Mentions list.
        // Basically, we have a reference problem relating to quantifiers. Quantifiers belong to the GrammarNode, but
        // the GrammarNode can appear in more than one place, with different quantifiers. This means we need to have copies
        // of the Nodes for the sake of the quantifier. But, if a GrammarNode is added before it is built, then only the original
        // will ever get built. So, we need to only build the original when the time comes, and then copy it out to its clones,
        // leaving their quantifiers intact, at the end of compilation.
        //****************************************************************************************************************************//
        private GrammarNode RegisterDelayedGrammarNode(Token ToRegister)
        {
            GrammarNode Top;
            if (!this.SingularNames[CurrentEnvironmentName].ContainsKey(ToRegister.Value))
            {
                SingularNames[CurrentEnvironmentName].Add(ToRegister.Value, new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE));
            }
            
            if (!this.Mentions[CurrentEnvironmentName].ContainsKey(ToRegister.Value))
            {
                Mentions[CurrentEnvironmentName].Add(ToRegister.Value, new List<GrammarNode>());
            }

            Top = SingularNames[CurrentEnvironmentName][ToRegister.Value].Copy();
            Mentions[CurrentEnvironmentName][ToRegister.Value].Add(Top);

            return Top;
        }

        

        private GrammarNode FillSequence(ref int i, TokenList Unit)
        {
            Token CurrentToken = Unit[i];
            Token PreviousToken = null;
            GrammarNode Sequence = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            
            while (CurrentToken.TokenType != "pipe" && !CurrentToken.IsEOF && CurrentToken.TokenType != "terminator")
            {
                switch (CurrentToken.TokenType)
                {
                    case "name":
                        Sequence.Add(RegisterDelayedGrammarNode(CurrentToken));
                        break;
                    case "string":
                        Sequence.Add(GrammarNode.ToSequence(CurrentToken.Value, GrammarNodeTypeQuantifier.ONE));
                        break;
                    case "regex":
                        string regex = CurrentToken.Value.Substring(1).Substring(CurrentToken.Value.Length - 2);
                        Sequence.Add(new GrammarNode(GrammarNodeType.TERMINAL, regex, MatchType.REGEX));
                        break;
                    case "quantifier":
                        switch (CurrentToken.Value)
                        {
                            case "+":
                                Sequence[Sequence.Count - 1].GNTQuantifier = GrammarNodeTypeQuantifier.ONE_OR_MORE;
                                break;
                            case "*":
                                Sequence[Sequence.Count - 1].GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_MORE;
                                break;
                            case "?":
                                Sequence[Sequence.Count - 1].GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_ONE;
                                break;
                            default:
                                throw new Exception("Unrecognized quantifier: " + CurrentToken.Value);
                        }
                        break;

                    default:
                        throw new Exception("Unrecognized sequence atom. Type: " + CurrentToken.TokenType + " Value: " + CurrentToken.Value);
                }
                
                i++;
                CurrentToken = Unit[i];
            }

            i++; // Set us up on either the start of the next sequence or off the end.
            return Sequence;
        }

        private void CopyChildrenToMentions()
        {
            List<string> Environments = new List<string>();
            Environments.Add("scanner");
            Environments.Add("parser");

            foreach (string EnvironmentName in Environments)
            {
                Dictionary<string, GrammarNode> Singulars = SingularNames[EnvironmentName];
                foreach (KeyValuePair<string, GrammarNode> Singular in Singulars)
                {
                    List<GrammarNode> Clones = Mentions[EnvironmentName][Singular.Key];
                    foreach (GrammarNode Clone in Clones)
                    {
                        Clone.AddRange(Singular.Value.ToList());
                    }
                }
            }
        }

        private void Cleanup()
        {
            CopyChildrenToMentions();
        }
    }

    [Serializable]
    public class Language
    {
        public List<string> ScannerNames;
        public Dictionary<string, GrammarNode> ScannerProductions;
        public List<string> ParserNames;
        public Dictionary<string, GrammarNode> ParserProductions;
        public Dictionary<string, string> Ignore;
        public string LanguageName;

        protected bool AddProduction(string EnvironmentName, string ProductionName, GrammarNode Production)
        {
            Dictionary<string, GrammarNode> ProductionDictToFill;
            List<string> NameListToFill;
            switch (EnvironmentName)
            {
                case "scanner":
                    ProductionDictToFill = ScannerProductions;
                    NameListToFill = ScannerNames;
                    break;
                case "parser":
                    ProductionDictToFill = ParserProductions;
                    NameListToFill = ParserNames;
                    break;
                default:
                    throw new Exception("Unknown environment name: " + EnvironmentName);
            }

            if (NameListToFill.Contains(ProductionName))
            {
                return false;
            }
            NameListToFill.Add(ProductionName);
            ProductionDictToFill.Add(ProductionName, Production);
            return true;
        }
    }

    
    [Serializable]
    public class EBNF : Language
    {
        public EBNF()
        {
            LanguageName = "EBNF";
            ScannerNames = new List<string>();
            ScannerProductions = new Dictionary<string, GrammarNode>();

            GrammarNode name = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode name_initial = new GrammarNode(GrammarNodeType.TERMINAL, "[a-z]", MatchType.REGEX);
            GrammarNode name_subsequent = new GrammarNode(GrammarNodeType.TERMINAL, "[a-z0-9_]", MatchType.REGEX, GrammarNodeTypeQuantifier.ZERO_OR_MORE);

            name.Add(name_initial);
            name.Add(name_subsequent);
            // TODO: Reserved words require lookahead for robust performance.
            //GrammarNode environment_name = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);
            //GrammarNode scanner_name = Language.ToSequence("scanner", GrammarNodeTypeQuantifier.ONE);
            //GrammarNode parser_name = Language.ToSequence("parser", GrammarNodeTypeQuantifier.ONE);
            //environment_name.Add(scanner_name);
            //environment_name.Add(parser_name);
            // On hold pending decision on and implementation of lookahead.
            //GrammarNode ignore_production_name = ToSequence("ignore", GrammarNodeTypeQuantifier.ONE);

           

            GrammarNode assignment = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode colon = new GrammarNode(GrammarNodeType.TERMINAL, ":", MatchType.VALUE);
            GrammarNode equal = new GrammarNode(GrammarNodeType.TERMINAL, "=", MatchType.VALUE);
            assignment.Add(colon);
            assignment.Add(equal);

            GrammarNode whitespace = new GrammarNode(GrammarNodeType.TERMINAL, @"\s", MatchType.REGEX, GrammarNodeTypeQuantifier.ONE_OR_MORE);

            // regex:= /\/([^\/]|(?<\\)\/)+\//;
            GrammarNode regex = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode regex_initial = new GrammarNode(GrammarNodeType.TERMINAL, "/", MatchType.VALUE);
            GrammarNode regex_terminal = new GrammarNode(GrammarNodeType.TERMINAL, "/", MatchType.VALUE);
            GrammarNode regex_piece = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ONE_OR_MORE);
            GrammarNode not_slashes = new GrammarNode(GrammarNodeType.TERMINAL, @"[^\\/]", MatchType.REGEX, GrammarNodeTypeQuantifier.ONE);
            GrammarNode escape = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ONE);
            GrammarNode regex_backslash = new GrammarNode(GrammarNodeType.TERMINAL, @"\", MatchType.VALUE, GrammarNodeTypeQuantifier.ONE);
            GrammarNode regex_thing_escaped = new GrammarNode(GrammarNodeType.TERMINAL, @"\S", MatchType.REGEX, GrammarNodeTypeQuantifier.ONE);

            // assemble production 'regex'
            regex.Add(regex_initial);
            regex.Add(regex_piece);
            regex.Add(regex_terminal);

            // assemble label 'regex_piece'
            regex_piece.Add(escape);
            regex_piece.Add(not_slashes);
            
            // assemble label 'escape'
            escape.Add(regex_backslash); // escape sequences start with a backslash...
            escape.Add(regex_thing_escaped); // ...and are followed by the thing escaped.

            GrammarNode terminator = new GrammarNode(GrammarNodeType.TERMINAL, ";", MatchType.VALUE);

            GrammarNode ebnf_string = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode quote = new GrammarNode(GrammarNodeType.TERMINAL, "\"", MatchType.VALUE);
            //string_element:= /[^\\"]/ | /\\\"/| /\\\\/;
            GrammarNode string_element = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ZERO_OR_MORE);
            GrammarNode not_slash_or_quote = new GrammarNode(GrammarNodeType.TERMINAL, @"[^\\" + "\"]", MatchType.REGEX);
            string_element.Add(not_slash_or_quote);
            string_element.Add(escape);
            ebnf_string.Add(quote);
            ebnf_string.Add(string_element);
            ebnf_string.Add(quote);

            GrammarNode quantifier = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);
            GrammarNode zero_or_one = new GrammarNode(GrammarNodeType.TERMINAL, "?", MatchType.VALUE);
            GrammarNode zero_or_more = new GrammarNode(GrammarNodeType.TERMINAL, "*", MatchType.VALUE);
            GrammarNode one_or_more = new GrammarNode(GrammarNodeType.TERMINAL, "+", MatchType.VALUE);
            quantifier.Add(zero_or_more);
            quantifier.Add(zero_or_one);
            quantifier.Add(one_or_more);

            GrammarNode comment = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode slash = new GrammarNode(GrammarNodeType.TERMINAL, "/", MatchType.VALUE);
            comment.Add(slash);
            comment.Add(slash);
            GrammarNode not_newline = new GrammarNode(GrammarNodeType.TERMINAL, @"[^\n\r]", MatchType.REGEX, GrammarNodeTypeQuantifier.ZERO_OR_MORE);
            comment.Add(not_newline);

            GrammarNode pipe = new GrammarNode(GrammarNodeType.TERMINAL, "|", MatchType.VALUE);


            GrammarNode language_name = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ONE);
            GrammarNode language_sigil = new GrammarNode(GrammarNodeType.TERMINAL, "%", MatchType.VALUE, GrammarNodeTypeQuantifier.ONE);
            GrammarNode Zor1whitespace = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ZERO_OR_ONE);
            Zor1whitespace.Add(whitespace);

            GrammarNode mixed_name = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode mixed_initial = new GrammarNode(GrammarNodeType.TERMINAL, "[a-zA-Z]", MatchType.REGEX);
            GrammarNode mixed_subsequent = new GrammarNode(GrammarNodeType.TERMINAL, "[a-zA-Z0-9_]", MatchType.REGEX, GrammarNodeTypeQuantifier.ZERO_OR_MORE);
            
            // assemble label 'mixed_name'
            mixed_name.Add(mixed_initial);
            mixed_name.Add(mixed_subsequent);

            // assemble production 'language_name'
            language_name.Add(language_sigil);
            language_name.Add(language_sigil);
            language_name.Add(Zor1whitespace);
            language_name.Add(mixed_name);

            GrammarNode environment_name = GrammarNode.SequenceNode();
            environment_name.Add(language_sigil);
            environment_name.Add(Zor1whitespace);
            environment_name.Add(name);

            GrammarNode eof = new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.EOF);
            
            AddProduction("scanner", "environment_name", environment_name);
            AddProduction("scanner", "name", name);
            AddProduction("scanner", "assignment", assignment);
            AddProduction("scanner","whitespace", whitespace);
            AddProduction("scanner","regex", regex);
            AddProduction("scanner","terminator", terminator);
            AddProduction("scanner","string", ebnf_string);
            AddProduction("scanner","quantifier", quantifier);
            AddProduction("scanner","comment", comment);
            AddProduction("scanner","pipe", pipe);
            AddProduction("scanner","language_name", language_name);
            AddProduction("scanner","EOF", eof);
            
            // The scanner won't send these off to the parser.
            this.Ignore = new Dictionary<string, string>();
            Ignore.Add("whitespace", "whitespace");
            Ignore.Add("comment", "comment");

            ParserNames = new List<string>();
            ParserProductions = new Dictionary<string, GrammarNode>();
        
            //language_specifier:= language_name;
            GrammarNode P_language_spec = new GrammarNode(GrammarNodeType.TERMINAL, "language_name", MatchType.TYPE);

            

            GrammarNode P_environment_name = new GrammarNode(GrammarNodeType.TERMINAL, "environment_name", MatchType.TYPE);

            //production:= production_name assignment sequence alternation_subsequent* terminator;
            GrammarNode production = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode P_name = GrammarNode.TerminalNodeByType("name");
            GrammarNode P_assignment = GrammarNode.TerminalNodeByType("assignment");
            GrammarNode P_sequence = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode P_alternation_subsequent = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ZERO_OR_MORE);
            GrammarNode P_terminator = new GrammarNode(GrammarNodeType.TERMINAL, "terminator", MatchType.TYPE);

            // assemble production 'production'
            production.Add(P_name);
            production.Add(P_assignment);
            production.Add(P_sequence);
            production.Add(P_alternation_subsequent);
            production.Add(P_terminator);

            

            //sequence:= sequence_atom+;
            GrammarNode P_sequence_atom = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE, GrammarNodeTypeQuantifier.ONE_OR_MORE);
            P_sequence.Add(P_sequence_atom);

            //sequence_atom:= quantified_name | quantified_string | regex;
            GrammarNode P_quantified_name = GrammarNode.SequenceNode();
            GrammarNode P_quantified_string = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode P_regex = GrammarNode.TerminalNodeByType("regex");
            P_sequence_atom.Add(P_quantified_name);
            P_sequence_atom.Add(P_quantified_string);
            P_sequence_atom.Add(P_regex);

            //quantified_name:= name quantifier?;
            GrammarNode P_quantifier = GrammarNode.TerminalNodeByType("quantifier");
            P_quantifier.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_ONE;
            P_quantified_name.Add(P_name);
            P_quantified_name.Add(P_quantifier);

            //quantified_string:= string quantifier?;
            GrammarNode P_string = GrammarNode.TerminalNodeByType("string"); 
            P_quantified_string.Add(P_string);
            P_quantified_string.Add(P_quantifier);
            
            //alternation_subsequent:= pipe sequence;
            GrammarNode P_pipe = GrammarNode.TerminalNodeByType("pipe");
            P_alternation_subsequent.Add(P_pipe);
            P_alternation_subsequent.Add(P_sequence);
            
            
            AddProduction("parser","production", production); 
            AddProduction("parser","language_spec", P_language_spec);
            AddProduction("parser","environment_name", P_environment_name);
            AddProduction("parser","EOF", eof);
        }
    }

    [Serializable]
    public class TestLanguage : Language
    {

        public TestLanguage()
        {
            this.LanguageName = "Lispish";
            // The scanner will understand tokens in the sentence "(one foo baz   )", for example.
            // Incidentally, it will also understand them in "())( zab (((()", but that's not the scanner's problem.
            ScannerProductions = new Dictionary<string, GrammarNode>();
            ScannerNames = new List<string>();
            GrammarNode SPName = new GrammarNode(GrammarNodeType.TERMINAL, "[a-z]", MatchType.REGEX);
            SPName.GNTQuantifier = GrammarNodeTypeQuantifier.ONE_OR_MORE;
            AddProduction("scanner","name", SPName);

            GrammarNode SPLParen = new GrammarNode(GrammarNodeType.TERMINAL, "(", MatchType.VALUE);
            SPLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            AddProduction("scanner","lparen", SPLParen);

            GrammarNode SPRParen = new GrammarNode(GrammarNodeType.TERMINAL, ")", MatchType.VALUE);
            SPLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            AddProduction("scanner","rparen", SPRParen);

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
            AddProduction("scanner","whitespace", SPWhiteSpace);

            GrammarNode SPEOF = new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.TYPE);
            SPEOF.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            AddProduction("scanner","EOF", SPEOF);


            // The parser will understand sentences of the form "(foo baz bar   one   )", for example.
            // It will also understand "(foo (baz bar)   one )". That is, recursive lists of simple alphabetic string atoms.

            ParserProductions = new Dictionary<string, GrammarNode>();
            ParserNames = new List<string>();
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

            //AddProduction("parser","whitespace", PWhitespace);
            AddProduction("parser","list", PList);
            AddProduction("parser","EOF", EOF);

            // The scanner will suppress whitespace, which will simplify parsing.
            Ignore = new Dictionary<string, string>();

            Ignore.Add("whitespace", "whitespace"); // It's only the key that matters.


        }
    }
}
