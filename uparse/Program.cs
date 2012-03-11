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
            

            //TestLanguage Lispish = new TestLanguage();
            String TestFile = @"..\..\..\Samples\test.lisp";
            StreamReader cs = new StreamReader(TestFile);
            String Code = cs.ReadToEnd();
            CodeStream code_tokens = new CodeStream(Code);
            String OutputFile = @"..\..\..\Samples\test.out.txt";


            //TestLanguage my_tl = new TestLanguage();
            Stream TestLanguageStream = File.OpenRead(@"..\..\..\Samples\persistence_test");
            BinaryFormatter bf = new BinaryFormatter();
            TestLanguage my_tl = (TestLanguage)bf.Deserialize(TestLanguageStream);
            Scanner SourceScanner = new Scanner(code_tokens, my_tl.ScannerProductions, my_tl.Ignore);
            Parser TestLanguageParser = new Parser(new DiagnosticCompiler(OutputFile), SourceScanner, my_tl.ParserProductions);
            TestLanguageStream.Close();

            TestLanguageParser.Parse();
            Stream TestLanguageFile = File.Create(@"..\..\..\Samples\persistence_test");
            //BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(TestLanguageFile, my_tl);
            Console.WriteLine(Directory.GetCurrentDirectory());
            Console.ReadKey();
            
        }
    }
}
