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

        public void Become(GrammarNode Target)
        {

            if (GrammarNodeTypeQuantifier.ONE == this.GNTQuantifier)
            {
                this.GNTQuantifier = Target.GNTQuantifier;
            }
            else if (GrammarNodeTypeQuantifier.ONE == Target.GNTQuantifier)
            {
                // NO-OP. Keep existing value.
            }
            else if (GrammarNodeTypeQuantifier.ZERO_OR_MORE == Target.GNTQuantifier || GrammarNodeTypeQuantifier.ZERO_OR_MORE == this.GNTQuantifier)
            {
                this.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_MORE;
            }
            else if (Target.GNTQuantifier == Target.GNTQuantifier)
            {
                // NO-OP again.
            }
            else
            {
                this.GNTQuantifier = GrammarNodeTypeQuantifier.ZERO_OR_MORE;
            }
            this.GNType = Target.GNType;
            this.MatchOn = Target.MatchOn;
            this.MatchText = Target.MatchText;

            this.RemoveRange(0, this.Count);

            foreach (GrammarNode gn in Target)
            {
                this.Add(gn);
            }
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

        internal static GrammarNode EOFNode()
        {
            return new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.EOF);
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
                //Console.WriteLine(tag + " trying to find " + name);
                if (AdvanceQuantified(Production))
                {
                    //Console.WriteLine(tag + ":");
                    //Console.WriteLine(Production.GNTQuantifier.ToString() + " " + name + " by " + Production.GNType.ToString() + " on " + Production.MatchOn.ToString());
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
                if (TokenPosition >= scanned.Count - 1 && scanned.Count >= 1 && scanned[scanned.Count - 1].IsEOF)
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
            target = Target;
            this.Checker = new Validator(SourceScanner, LanguageNames, LanguageProductions);
            tag = "Parser";
            Checker.tag = this.tag;
            
        }
        private string tag;
        private ICompiler target;
        public ICompiler Target
        {
            get
            {
                return target;
            }
            set
            {
                target = value;
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
                    //Target.Build(Checker.GetProduction());
                    break;
                }
            }
            this.Target.CleanUp();
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
        void CleanUp();
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
        public void CleanUp()
        {
            return;
        }

    }

    //TODO: There's a ton of extranneous nesting going on, over and beyond just having a non-optimal compilation.

    public class ParserCompiler : ICompiler
    {
        private List<string> ScannerNames;
        private List<string> ParserNames;
        private Dictionary<string, GrammarNode> ScannerProductions;
        private Dictionary<string, GrammarNode> ParserProductions;
        private Dictionary<string, string> Ignore;
        private string LanguageName;
        private Dictionary<string, Dictionary<string, GrammarNode>> Templates;
        private Dictionary<string, Dictionary<string, List<GrammarNode>>> Instances;

        private string CurrentEnvironment;
        
        public ParserCompiler(string Output)
        {
            ofile = Output;
            ScannerNames = new List<string>();
            ParserNames = new List<string>();
            ScannerProductions = new Dictionary<string,GrammarNode>();
            ParserProductions = new Dictionary<string,GrammarNode>();
            Ignore = new Dictionary<string,string>();
            CurrentEnvironment = null;
            LanguageName = null;
            Templates = new Dictionary<string,Dictionary<string,GrammarNode>>();
            Templates["scanner"] = new Dictionary<string,GrammarNode>();
            Templates["parser"] = new Dictionary<string,GrammarNode>();
            Instances = new Dictionary<string,Dictionary<string,List<GrammarNode>>>();
            Instances["scanner"] = new Dictionary<string,List<GrammarNode>>();
            Instances["parser"] = new Dictionary<string,List<GrammarNode>>();
        }

        private string ofile;
        
        #region ICompiler Members
        public void CleanUp()
        {
            AppendEOFNodes();
            CopyTemplatesToInstances();
            Optimize();
            Persist();
        }

        public void Build(TokenList Unit)
        {
            switch (Unit.tag)
            {
                case "production":
                    BuildProduction(Unit);
                    break;
                case "language_specifier":
                    LanguageName = Unit[0].Value.Replace("%", "").Replace(" ", "");
                    break;
                case "environment_specifier":
                    EnterEnvironment(Unit[0].Value.Replace("%", "").Replace(" ", ""));
                    break;
                case "EOF":
                    CleanUp();
                    break;
                default:
                    throw new Exception("Unexpected construct: " + Unit.tag);
            }
        }

        private void BuildProduction(TokenList Production)
        {
            Console.WriteLine("Building " + Production.tag + ", starting with " + Production[0].Value);

            GrammarNode Top = TemplateForInstanceName(Production[0].Value);
            Dictionary<string, GrammarNode> ProductionList;
            switch (CurrentEnvironment)
            {
                case "scanner":
                    ProductionList = ScannerProductions;
                    break;
                case "parser":
                    ProductionList = ParserProductions;
                    break;
                default:
                    throw new Exception("Impossible compiler environment: " + CurrentEnvironment);
            }
            
            Token Head = Production[0];
            switch (Head.Value)
            {
                case "export":
                    FillNames(Production, Production[0].Value);
                    break;
                case "ignore":
                    FillNames(Production, Production[0].Value);
                    break;
                default:
                    ProductionList.Add(Production[0].Value, Top);
                    int i = 2; // 3rd element in a production is the first token to be put into a sequence, right after the ":="

                    while (i < Production.Count - 1) // don't try to build terminator...
                    {
                        AddSequence(Top, Production, ref i);
                    } 
                    break;
            }
           
        }
        
        private void AddSequence(GrammarNode Template, TokenList Production, ref int Location)
        {
            GrammarNode Sequence = new GrammarNode(GrammarNodeType.SEQUENCE, "", MatchType.RECURSE);
            Template.Add(Sequence);
            Token CurrentToken = Production[Location];
            Console.WriteLine("Adding sequence...");
            //Console.ReadKey();
            while (CurrentToken.TokenType != "pipe" && CurrentToken.TokenType != "terminator")
            {
                Console.WriteLine(CurrentToken.TokenType + " : " + CurrentToken.Value);
                //Console.ReadKey();
                switch (CurrentToken.TokenType)
                {
                    case "name":
                        if ("parser" == CurrentEnvironment && ScannerNames.Contains(CurrentToken.Value))
                        {
                            Sequence.Add(GrammarNode.TerminalNodeByType(CurrentToken.Value));
                        }
                        else
                        {
                            Sequence.Add(InstanceForInstanceName(CurrentToken.Value));
                        }
                        break;
                    case "string":
                        Sequence.Add(GrammarNode.ToSequence(StringConverter.UnEscape(StringConverter.StringContents(CurrentToken.Value)), GrammarNodeTypeQuantifier.ONE));
                        break;
                    case "regex":
                        Sequence.Add(
                            new GrammarNode(GrammarNodeType.TERMINAL,
                                            CurrentToken.Value.Substring(1, CurrentToken.Value.Length - 2),
                                            MatchType.REGEX));
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
                        throw new Exception("Unrecognized sequence atom of type " + CurrentToken.TokenType + " and value " + CurrentToken.Value);
                }
                Location++;
                CurrentToken = Production[Location];
            }
            Location++; // skip the pipes and walk off the terminator
        }

        
        // Handle "export" and "ignore" productions, which are simple alternations.

        private void FillNames(TokenList SpecialProduction, string IgnoreOrExport)
        {
            Console.WriteLine("Building special production with name: " + SpecialProduction[0].Value);
            switch (IgnoreOrExport)
            {
                case "ignore":
                    if ("parser" == CurrentEnvironment)
                    {
                        throw new Exception("Ignore lists not allowed in Parser. Ignore tokens at scanner level.");
                    }
                    for (int i = 2; i < SpecialProduction.Count - 1; i += 2)
                    {
                        Console.WriteLine("Adding " + SpecialProduction[i].Value + " to Ignore.");
                        this.Ignore.Add(SpecialProduction[i].Value, SpecialProduction[i].Value);
                    }
                    break;
                case "export":
                    List<string> NameListToFill = new List<string>();
                    switch (CurrentEnvironment)
                    {
                        case "scanner":
                            NameListToFill = ScannerNames;
                            break;
                        case "parser":
                            NameListToFill = ParserNames;
                            break;
                        default:
                            throw new Exception("Impossibly unrecognized parser component: " + CurrentEnvironment);
                            
                    }

                    for (int i = 2; i < SpecialProduction.Count - 1; i += 2)
                    {
                        Console.WriteLine("Adding " + SpecialProduction[i].Value + " to " + NameListToFill.ToString());
                        NameListToFill.Add(SpecialProduction[i].Value);
                    }
                    break;
                default:
                    throw new Exception("Impossibly unrecognized special production: " + IgnoreOrExport);
            }
        }


        private void EnterEnvironment(string EnvironmentName)
        {
            if ("scanner" == EnvironmentName || "parser" == EnvironmentName)
            {
                CurrentEnvironment = EnvironmentName;
            }
            else
            {
                throw new Exception("Unknown environment name: " + EnvironmentName);
            }
        }

        private GrammarNode TemplateForInstanceName(string InstanceName)
        {
            Dictionary<string, GrammarNode> CurrentTemplates = Templates[CurrentEnvironment];
            // The template always goes in the current environment *because* 
            // we encounter and create the template at the beginning of a production
            
            if ((CurrentEnvironment == "parser" && Templates["parser"].ContainsKey(InstanceName)) || Templates["scanner"].ContainsKey(InstanceName))
            {
                if ("export" != InstanceName)
                {
                    throw new Exception("Duplicate name detected: " + InstanceName);
                }
            }
            return CurrentTemplates[InstanceName] = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);
        }

        private GrammarNode InstanceForInstanceName(string InstanceName)
        {
            Dictionary<string, List<GrammarNode>> CurrentInstances = Instances[CurrentEnvironment];
            GrammarNode Instance = new GrammarNode(GrammarNodeType.ALTERNATION, "", MatchType.RECURSE);
            if ("scanner" == CurrentEnvironment)
            {
                if (!CurrentInstances.ContainsKey(InstanceName))
                {
                    CurrentInstances[InstanceName] = new List<GrammarNode>();
                }
                CurrentInstances[InstanceName].Add(Instance);
            }
            else 
            {
                // TODO: This code should no longer be exercised. It should be preempted
                //       by an upstream check that inserts a token for the production name.
                Dictionary<string, List<GrammarNode>> ScannerInstances = Instances["scanner"];
                if (!CurrentInstances.ContainsKey(InstanceName) && !ScannerInstances.ContainsKey(InstanceName))
                {
                    CurrentInstances[InstanceName] = new List<GrammarNode>();
                    CurrentInstances[InstanceName].Add(Instance);
                }
                else if (ScannerInstances.ContainsKey(InstanceName))
                {
                    ScannerInstances[InstanceName].Add(Instance);
                }
                else // ParserInstances has a list already
                {
                    CurrentInstances[InstanceName].Add(Instance);
                }
            }
            
            return Instance;  
        }

        private void CopyTemplatesToInstances()
        {
            // All we have to do is add the children of each template to every instance, since the instances already have
            // updated quantifier info.

            foreach (KeyValuePair<string, Dictionary<string, List<GrammarNode>>> NamedInstanceListDict in Instances)
            {
                foreach (KeyValuePair<string, List<GrammarNode>> NamedInstanceList in NamedInstanceListDict.Value)
                {
                    foreach (GrammarNode Instance in NamedInstanceList.Value)
                    {
                        if (Templates["scanner"].ContainsKey(NamedInstanceList.Key))
                        {
                            FillInstanceFromTemplate(Instance, Templates["scanner"][NamedInstanceList.Key]);
                        }
                        else if (Templates["parser"].ContainsKey(NamedInstanceList.Key))
                        {
                            FillInstanceFromTemplate(Instance, Templates["parser"][NamedInstanceList.Key]);
                        }
                        else
                        {
                            throw new Exception("Instance \"" + NamedInstanceList.Key + "\" has no template.");
                        }
                    }
                }
            }
            Console.WriteLine("Copied templates out...");
        }

        private void FillInstanceFromTemplate(GrammarNode Instance, GrammarNode Template)
        {
            Instance.AddRange(Template.ToList());
        }

        private void Persist()
        {
            Language ToPersist = new Language();
            ToPersist.Ignore = this.Ignore;
            ToPersist.LanguageName = this.LanguageName;
            ToPersist.ScannerNames = this.ScannerNames;
            ToPersist.ScannerProductions = this.ScannerProductions;
            ToPersist.ParserNames = this.ParserNames;
            ToPersist.ParserProductions = this.ParserProductions;

            Stream PersistenceStream = File.Create(OutputFile);
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(PersistenceStream, ToPersist);
            PersistenceStream.Flush();
            PersistenceStream.Close();
        }

        

        private void Optimize()
        {
            Compact(ScannerProductions);
            Compact(ParserProductions);
            return;
        }

        private void Compact(Dictionary<string, GrammarNode> Productions)
        {
            foreach (KeyValuePair<string, GrammarNode> ToCompact in Productions)
            {
                CompactProduction(ToCompact.Value);
            }
        }

        private void CompactProduction(GrammarNode ToCompact)
        {
            foreach (GrammarNode child in ToCompact)
            {
                CompactProduction(child);
            }

            // If we have no children, or more than one child, everything is fine. Having no
            // children means we're a terminal of some kind, and having multiple children
            // means we're a non-trivial alternation or sequence. So, the only NO-OP is the
            // trivial case, where we have one child. In that case, we need to become the child
            // and adopt its children.
            if (1 == ToCompact.Count)
            {
                // Become the child.
                GrammarNode Target = ToCompact[0];
                ToCompact.Become(Target);

            }
            return;
        }

        private void AppendEOFNodes()
        {
            if (!ScannerProductions.ContainsKey("EOF"))
            {
                ScannerProductions.Add("EOF", GrammarNode.EOFNode());
            }

            if (!ParserProductions.ContainsKey("EOF"))
            {
                ParserProductions.Add("EOF", GrammarNode.EOFNode());
            }

            if (!ScannerNames.Contains("EOF"))
            {
                ScannerNames.Add("EOF");
            }
        }

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
    }

    public class StringConverter
    {
        public static string StringContents(string QuotedString)
        {
            return Regex.Replace(QuotedString, "^\"|\"$", "");
        }

        public static string UnEscape(string HasEscapes)
        {
            StringBuilder Cleansed = new StringBuilder("");

            for (int i = 0; i < HasEscapes.Length; i++)
            {
                switch (HasEscapes[i])
                {
                    case '\\':
                        Cleansed.Append(ConsumeEscape(ref i, HasEscapes));
                        break;
                    default:
                        Cleansed.Append(HasEscapes[i]);
                        break;
                }
            }

            
            return Cleansed.ToString();
        }

        private static string ConsumeEscape(ref int i, string HasEscapes)
        {
            i++;
            switch (HasEscapes[i])
            {
                case 'u':
                    string Escaped = HasEscapes.Substring(i + 1, 4);
                    i += 5;
                    return Escaped;
                case '\\':
                    i++;
                    return "\\";
                case '"':
                    i++;
                    return "\"";
                case '0':
                    i++;
                    return "\0";
                case 'a':
                    i++;
                    return "\a";
                case 'b':
                    i++;
                    return "\b";
                case 'f':
                    i++;
                    return "\f";
                case 'n':
                    i++;
                    return "\n";
                case 'r':
                    i++;
                    return "\r";
                case 't':
                    i++;
                    return "\t";
                case 'v':
                    return "\v";
                default:
                    throw new Exception("Unrecognized escape sequence \"\\" + HasEscapes[i].ToString() + "\"");
            }

        }
    }
    
}
