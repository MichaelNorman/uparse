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
            // The preparation:
            CodeStream code_tokens = new CodeStream(File.OpenText(@"..\..\..\Samples\ebnf_ebnf.txt").ReadToEnd());
            //EBNF ebnf = new EBNF();
            Stream ebnfStream = File.OpenRead(@"..\..\..\Samples\compiled_ebnf.persist");
            BinaryFormatter BFReader = new BinaryFormatter();
            Language ebnf = (Language)BFReader.Deserialize(ebnfStream);
            ebnfStream.Flush();
            ebnfStream.Close();

            Scanner SourceScanner = new Scanner(code_tokens, ebnf.ScannerNames, ebnf.ScannerProductions, ebnf.Ignore);
            Parser EBNFLanguageParser = new Parser(new ParserCompiler(@"..\..\..\Samples\compiled_ebnf.persist"),
                                                   SourceScanner,
                                                   ebnf.ParserNames,
                                                   ebnf.ParserProductions);

            // The business:
            EBNFLanguageParser.Parse();

            Console.WriteLine("Done. Press any key to continue...");
            Console.ReadKey();
        }
    }
}
