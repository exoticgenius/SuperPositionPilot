using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

using System.Text;
using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Concurrent;
using System.Reflection.Emit;
using System.Reflection.PortableExecutable;
using System.Reflection;
using System.Threading.Tasks;
using System.Diagnostics;
using SPL;

namespace SourceGeneratorSamples
{
    [Generator]
    public class HelloWorldGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            //treesWithlassWithAttributes[0].DescendantNodes().OfType<MethodDeclarationSyntax>().ToList()[0].DescendantNodes().ToList()[0]
            //((treesWithlassWithAttributes[0].DescendantNodes().OfType<MethodDeclarationSyntax>().ToList()[0] as MethodDeclarationSyntax).ReturnType as TypeSyntax)
            var treesWithlassWithAttributes = context.Compilation.SyntaxTrees
                .SelectMany(st =>
                    st.GetRoot()
                        .DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .Where(p => p.DescendantNodes().OfType<MethodDeclarationSyntax>().Any()))
                .ToList();

            Debug.WriteLine("DD");
            Debug.WriteLine("DD");
            Debug.WriteLine("DD");
            Debug.WriteLine("DD");
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required
        }
    }

}