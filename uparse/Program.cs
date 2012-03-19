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
            String ebnf_source_file_path = @"..\..\..\Samples\ebnf_ebnf.txt";
            StreamReader cs = new StreamReader(ebnf_source_file_path);
            String Code = cs.ReadToEnd();
            CodeStream code_tokens = new CodeStream(Code);
            String OutputFile = @"..\..\..\Samples\ebnf.out.txt";
            //StreamWriter of = File.CreateText(OutputFile);
            //of.AutoFlush = true;


            EBNF ebnf = new EBNF();
            //Stream ebnf_persistence_file_stream = File.OpenRead(@"..\..\..\Samples\ebnf_persist");
            //BinaryFormatter bf = new BinaryFormatter();
            //TestLanguage my_tl = (TestLanguage)bf.Deserialize(TestLanguageStream);
            Scanner SourceScanner = new Scanner(code_tokens, ebnf.ScannerNames,ebnf.ScannerProductions, ebnf.Ignore);
            //Parser EBNFLanguageParser = new Parser(new DiagnosticCompiler(OutputFile), SourceScanner, ebnf.ParserNames, ebnf.ParserProductions);
            Parser EBNFLanguageParser = new Parser(new ParserCompiler(@"..\..\..\Samples\ebnf.persist"), SourceScanner, ebnf.ParserNames, ebnf.ParserProductions);
            //TestLanguageStream.Close();

            EBNFLanguageParser.Parse();

            /*while (true)
            {
                Token current = SourceScanner.ReadToken();
                Console.WriteLine(current.TokenType + " : " + current.Value);
                of.WriteLine(current.TokenType + " : " + current.Value);
                if (SourceScanner.EOF)
                {
                    break;
                }
            }*/
            Stream ebnf_persistence_file_stream = File.Create(@"..\..\..\Samples\persistence_test");
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(ebnf_persistence_file_stream, ebnf);
            Console.WriteLine(Directory.GetCurrentDirectory());
            Console.ReadKey();
            
        }
    }
}
