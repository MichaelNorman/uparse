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
        public string tag;
        public Validator(ITokenStream Source, Dictionary<String, GrammarNode> Productions)
        {
            this.Source = Source;
            this.Productions = Productions;
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

            foreach (KeyValuePair<String, GrammarNode> kvp in Productions)
            {
                Console.WriteLine(tag + " trying to find " + kvp.Key);
                if (AdvanceQuantified(kvp.Value))
                {
                    Console.WriteLine(tag + ":");
                    Console.WriteLine(kvp.Value.GNTQuantifier.ToString() + " " + kvp.Key + " by " + kvp.Value.GNType.ToString() + " on " + kvp.Value.MatchOn.ToString());
                    return new TokenList(kvp.Key, Source.Range(StartPosition, Source.Position - StartPosition));
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
        private Dictionary<String, GrammarNode> Ignore;
        private string tag;
        public Scanner(ITokenStream Code, Dictionary<String, GrammarNode> Productions, Dictionary<string, GrammarNode> Ignore)
        {
            CodeValidator = new Validator(Code, Productions);
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
        public Parser(ICompiler Target, Scanner SourceScanner, Dictionary<string, GrammarNode> LanguageProductions)
        {
            target = (DiagnosticCompiler)Target;
            this.Checker = new Validator(SourceScanner, LanguageProductions);
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
        #region ICompiler Members

        public void Build(TokenList Unit)
        {
            throw new NotImplementedException();
        }

        public string OutputFile
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }

    [Serializable]
    public class Language
    {
        public Dictionary<string, GrammarNode> ScannerProductions;
        public Dictionary<string, GrammarNode> ParserProductions;
        public Dictionary<string, GrammarNode> Ignore;

        static protected GrammarNode ToSequence(string ToSequence,  GrammarNodeTypeQuantifier NumTimes)
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

        static protected GrammarNode TerminalNodeByType(string MatchText)
        {
            return new GrammarNode(GrammarNodeType.TERMINAL, MatchText, MatchType.TYPE);
        }

        static protected GrammarNode SequenceNode()
        {
            return new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
        }

    }

    
    [Serializable]
    public class EBNF : Language
    {
        public EBNF()
        {

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

            GrammarNode environment_name = Language.SequenceNode();
            environment_name.Add(language_sigil);
            environment_name.Add(Zor1whitespace);
            environment_name.Add(name);

            GrammarNode eof = new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.EOF);

            ScannerProductions.Add("environment_name", environment_name);
            ScannerProductions.Add("name", name);
            ScannerProductions.Add("assignment", assignment);
            ScannerProductions.Add("whitespace", whitespace);
            ScannerProductions.Add("regex", regex);
            ScannerProductions.Add("terminator", terminator);
            ScannerProductions.Add("string", ebnf_string);
            ScannerProductions.Add("quantifier", quantifier);
            ScannerProductions.Add("comment", comment);
            ScannerProductions.Add("pipe", pipe);
            ScannerProductions.Add("language_name", language_name);
            ScannerProductions.Add("EOF", eof);
            
            // The scanner won't send these off to the parser.
            this.Ignore = new Dictionary<string, GrammarNode>();
            Ignore.Add("whitespace", whitespace);
            Ignore.Add("comment", comment);

            this.ParserProductions = new Dictionary<string, GrammarNode>();
        
            //language_specifier:= language_name;
            GrammarNode P_language_spec = new GrammarNode(GrammarNodeType.TERMINAL, "language_name", MatchType.TYPE);

            

            GrammarNode P_environment_name = new GrammarNode(GrammarNodeType.TERMINAL, "environment_name", MatchType.TYPE);

            //production:= production_name assignment sequence alternation_subsequent* terminator;
            GrammarNode production = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode P_name = Language.TerminalNodeByType("name");
            GrammarNode P_assignment = Language.TerminalNodeByType("assignment");
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
            GrammarNode P_quantified_name = Language.SequenceNode();
            GrammarNode P_quantified_string = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            GrammarNode P_regex = Language.TerminalNodeByType("regex");
            P_sequence_atom.Add(P_quantified_name);
            P_sequence_atom.Add(P_quantified_string);
            P_sequence_atom.Add(P_regex);

            //quantified_name:= name quantifier?;
            GrammarNode P_quantifier = Language.TerminalNodeByType("quantifier");
            P_quantifier.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_ONE;
            P_quantified_name.Add(P_name);
            P_quantified_name.Add(P_quantifier);

            //quantified_string:= string quantifier?;
            GrammarNode P_string = Language.TerminalNodeByType("string"); 
            P_quantified_string.Add(P_string);
            P_quantified_string.Add(P_quantifier);
            
            //alternation_subsequent:= pipe sequence;
            GrammarNode P_pipe = Language.TerminalNodeByType("pipe");
            P_alternation_subsequent.Add(P_pipe);
            P_alternation_subsequent.Add(P_sequence);
            

            ParserProductions.Add("production", production); 
            ParserProductions.Add("language_spec", P_language_spec);
            ParserProductions.Add("environment_name", P_environment_name);
            ParserProductions.Add("EOF", eof);
        }
    }

    [Serializable]
    public class TestLanguage : Language
    {

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
