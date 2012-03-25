using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Text.RegularExpressions;
using System.Runtime.Serialization.Formatters.Binary;


namespace UniversalParser
{
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
            AddProduction("scanner", "whitespace", whitespace);
            AddProduction("scanner", "regex", regex);
            AddProduction("scanner", "terminator", terminator);
            AddProduction("scanner", "string", ebnf_string);
            AddProduction("scanner", "quantifier", quantifier);
            AddProduction("scanner", "comment", comment);
            AddProduction("scanner", "pipe", pipe);
            AddProduction("scanner", "language_name", language_name);
            AddProduction("scanner", "EOF", eof);

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


            AddProduction("parser", "production", production);
            AddProduction("parser", "language_specifier", P_language_spec);
            AddProduction("parser", "environment_specifier", P_environment_name);
            AddProduction("parser", "EOF", eof);
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
            AddProduction("scanner", "name", SPName);

            GrammarNode SPLParen = new GrammarNode(GrammarNodeType.TERMINAL, "(", MatchType.VALUE);
            SPLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            AddProduction("scanner", "lparen", SPLParen);

            GrammarNode SPRParen = new GrammarNode(GrammarNodeType.TERMINAL, ")", MatchType.VALUE);
            SPLParen.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            AddProduction("scanner", "rparen", SPRParen);

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
            AddProduction("scanner", "whitespace", SPWhiteSpace);

            GrammarNode SPEOF = new GrammarNode(GrammarNodeType.TERMINAL, "EOF", MatchType.TYPE);
            SPEOF.GNTQuantifier = GrammarNodeTypeQuantifier.ONE;
            AddProduction("scanner", "EOF", SPEOF);


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
            AddProduction("parser", "list", PList);
            AddProduction("parser", "EOF", EOF);

            // The scanner will suppress whitespace, which will simplify parsing.
            Ignore = new Dictionary<string, string>();

            Ignore.Add("whitespace", "whitespace"); // It's only the key that matters.


        }
    }
    
}