using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UniversalParser;

namespace uparse
{
    class Program
    {
        static void Main(string[] args)
        {
            //String OutputPath, ICompiler Target, Scanner SourceScanner, Dictionary<string, GrammarNode> LanguageProductions, List<GrammarNode> IgnoreList
            //TestLanguage Lispish = new TestLanguage();
            String TestFile = @"C:\scripts\uparse\test.lisp";
            StreamReader cs = new StreamReader(TestFile);
            String Code = cs.ReadToEnd();
            CodeStream code_tokens = new CodeStream(Code);
            String OutputFile = @"C:\scripts\uparse\test.out.txt";
            //DiagnosticCompiler Target = new DiagnosticCompiler(OutputFile);



            TestLanguage my_tl = new TestLanguage();
            Stream TestLanguageStream = File.OpenRead(@"C:\scripts\uparse\persistence_test");
            BinaryFormatter bf = new BinaryFormatter();
            //TestLanguage my_tl = (TestLanguage)bf.Deserialize(TestLanguageStream);
            Scanner SourceScanner = new Scanner(code_tokens, my_tl.ScannerProductions, my_tl.Ignore);
            Parser TestLanguageParser = new Parser(new DiagnosticCompiler(OutputFile), SourceScanner, my_tl.ParserProductions);
            TestLanguageStream.Close();

            TestLanguageParser.Parse();
            Stream TestLanguageFile = File.Create(@"C:\scripts\uparse\persistence_test");
            //BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(TestLanguageFile, my_tl);
            Console.ReadKey();
            //Validator my_validator = new Validator(code_tokens, my_tl.ScannerProductions);

            /*while (true)
            {
                TokenList production = my_validator.GetProduction();

                Console.WriteLine("BEGINPRODUCTION : " + production.tag);
                foreach (Token tok in production)
                {
                    Console.WriteLine(tok.TokenType + " : " + tok.Value);
                }
                Console.WriteLine("ENDPRODUCTION : " + production.tag);
                if (my_validator.EOF)
                {
                    break;
                }
            }*/
            /*int i = 0;
            while (!code_tokens.EOF)
            {
                Token myToken = code_tokens.ReadToken();//[i];
                Console.WriteLine(myToken.TokenType + " : " + myToken.Value);
                i++;
            }*/
            //Console.ReadKey();
            //Parser SimpleLispParser = new Parser(OutputFile, Target, SourceScanner, Lispish.ParserProductions, Lispish.ScannerIgnore);
            //SimpleLispParser.Parse();
        }
    }
}
