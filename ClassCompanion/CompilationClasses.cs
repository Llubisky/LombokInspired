using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClassCompanion
{
    // Clase para combinar la compilación con las clases recolectadas
    public class CompilationClasses
    {
        public Compilation Compilation { get; set; }
        public ImmutableArray<ClassDeclarationSyntax> Classes { get; set; }
    }
}
