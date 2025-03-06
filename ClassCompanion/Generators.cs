using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace ClassCompanion
{
    [Generator]
    public class IncrementalGenerators : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Proveedor de nodos de sintaxis: todas las clases con al menos un atributo.
            IncrementalValuesProvider<ClassDeclarationSyntax> classDeclarations = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: static (Microsoft.CodeAnalysis.SyntaxNode node, CancellationToken cancellationToken) =>
                    {
                        if (node is ClassDeclarationSyntax classDecl && classDecl.AttributeLists.Count > 0)
                        {
                            return true;
                        }
                        return false;
                    },
                    transform: static (GeneratorSyntaxContext ctx, CancellationToken cancellationToken) =>
                    {
                        return (ClassDeclarationSyntax)ctx.Node;
                    })
                .Where(static (ClassDeclarationSyntax classDecl) => classDecl is not null);

            // Combinar la compilación con las clases recolectadas, sin utilizar tuplas en la salida.
            IncrementalValueProvider<CompilationClasses> compilationAndClasses = context.CompilationProvider
                .Combine(classDeclarations.Collect())
                .Select(static (Pair, cancellationToken) =>
                {
                    CompilationClasses cc = new CompilationClasses();
                    cc.Compilation = Pair.Left;
                    cc.Classes = Pair.Right;
                    return cc;
                });

            // Registrar la salida del generador.
            context.RegisterSourceOutput(compilationAndClasses, (SourceProductionContext spc, CompilationClasses compilationClasses) =>
            {
                Compilation compilation = compilationClasses.Compilation;
                ImmutableArray<ClassDeclarationSyntax> classDeclarationsCollected = compilationClasses.Classes;

                foreach (ClassDeclarationSyntax classDecl in classDeclarationsCollected)
                {
                    SemanticModel model = compilation.GetSemanticModel(classDecl.SyntaxTree);
                    ISymbol symbol = model.GetDeclaredSymbol(classDecl);
                    if (!(symbol is INamedTypeSymbol classSymbol))
                    {
                        continue;
                    }

                    // Obtener los símbolos de los atributos.
                    INamedTypeSymbol allArgsConstructorAttributeSymbol = compilation.GetTypeByMetadataName("ClassCompanion.AllArgsConstructorAttribute");
                    INamedTypeSymbol builderAttributeSymbol = compilation.GetTypeByMetadataName("ClassCompanion.BuilderAttribute");

                    // Si ninguno de los atributos está presente, se omite la clase.
                    if (allArgsConstructorAttributeSymbol == null && builderAttributeSymbol == null)
                    {
                        continue;
                    }

                    AttributesConfiguration config = EvaluateAttributes(classSymbol, allArgsConstructorAttributeSymbol, builderAttributeSymbol);
                    if (config.AplicarAllArgs == false && config.AplicarBuilder == false)
                    {
                        continue;
                    }

                    StringBuilder sourceBuilder = new StringBuilder();
                    AppendNamespaceOpening(classSymbol, sourceBuilder);

                    sourceBuilder.AppendLine("partial class " + classSymbol.Name);
                    sourceBuilder.AppendLine("{");

                    // Generar constructor AllArgs.
                    if (config.AplicarAllArgs)
                    {
                        List<MemberInfo> allArgsMembers = GetMembers(classSymbol, config.IncludePropertiesAllArgs);
                        GenerateAllArgsConstructor(classSymbol, allArgsMembers, sourceBuilder);
                    }

                    // Generar la clase Builder.
                    if (config.AplicarBuilder)
                    {
                        List<MemberInfo> builderMembers = GetMembers(classSymbol, config.IncludePropertiesBuilder);
                        GenerateBuilderClass(classSymbol, builderMembers, config.BuilderMethodPrefix, sourceBuilder);

                        if (config.ToBuilder)
                        {
                            GenerateToBuilderMethod(classSymbol, builderMembers, config.BuilderMethodPrefix, sourceBuilder);
                        }
                    }

                    sourceBuilder.AppendLine("}");
                    AppendNamespaceClosing(classSymbol, sourceBuilder);

                    spc.AddSource(classSymbol.Name + "_Decorators.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
                }
            });
        }

        // Evalúa los atributos aplicados a la clase y devuelve una instancia de AttributesConfiguration.
        private static AttributesConfiguration EvaluateAttributes(INamedTypeSymbol classSymbol, INamedTypeSymbol allArgsConstructorAttributeSymbol, INamedTypeSymbol builderAttributeSymbol)
        {
            AttributesConfiguration config = new AttributesConfiguration();
            config.AplicarAllArgs = false;
            config.AplicarBuilder = false;
            config.ToBuilder = false;
            config.IncludePropertiesAllArgs = false;
            config.IncludePropertiesBuilder = false;
            config.BuilderMethodPrefix = "Set";

            foreach (AttributeData attribute in classSymbol.GetAttributes())
            {
                if (allArgsConstructorAttributeSymbol != null &&
                    attribute.AttributeClass?.Equals(allArgsConstructorAttributeSymbol, SymbolEqualityComparer.Default) == true)
                {
                    config.AplicarAllArgs = true;
                    foreach (KeyValuePair<string, TypedConstant> namedArg in attribute.NamedArguments)
                    {
                        if (namedArg.Key == "IncludeProperties" && namedArg.Value.Value is bool booleanValue)
                        {
                            config.IncludePropertiesAllArgs = booleanValue;
                        }
                    }
                }
                if (builderAttributeSymbol != null &&
                    attribute.AttributeClass?.Equals(builderAttributeSymbol, SymbolEqualityComparer.Default) == true)
                {
                    config.AplicarBuilder = true;
                    if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is bool boolPos)
                    {
                        config.ToBuilder = boolPos;
                    }
                    foreach (KeyValuePair<string, TypedConstant> namedArg in attribute.NamedArguments)
                    {
                        if (namedArg.Key == "ToBuilder" && namedArg.Value.Value is bool boolNamed)
                        {
                            config.ToBuilder = boolNamed;
                        }
                        if (namedArg.Key == "IncludeProperties" && namedArg.Value.Value is bool boolProp)
                        {
                            config.IncludePropertiesBuilder = boolProp;
                        }
                        if (namedArg.Key == "BuilderMethodPrefix" && namedArg.Value.Value is string prefix)
                        {
                            config.BuilderMethodPrefix = prefix;
                        }
                    }
                }
            }
            return config;
        }

        // Obtiene la lista de miembros (campos y, opcionalmente, propiedades) no estáticos y explícitos.
        private static List<MemberInfo> GetMembers(INamedTypeSymbol classSymbol, bool includeProperties)
        {
            List<MemberInfo> members = new List<MemberInfo>();

            foreach (IFieldSymbol field in classSymbol.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic == false && field.IsImplicitlyDeclared == false)
                {
                    MemberInfo member = new MemberInfo();
                    member.Name = field.Name;
                    member.Type = field.Type;
                    members.Add(member);
                }
            }

            if (includeProperties)
            {
                foreach (IPropertySymbol prop in classSymbol.GetMembers().OfType<IPropertySymbol>())
                {
                    if (prop.IsStatic == false && prop.IsImplicitlyDeclared == false && prop.Parameters.Length == 0)
                    {
                        MemberInfo member = new MemberInfo();
                        member.Name = prop.Name;
                        member.Type = prop.Type;
                        members.Add(member);
                    }
                }
            }
            return members;
        }

        // Escribe la apertura del namespace en el StringBuilder.
        private static void AppendNamespaceOpening(INamedTypeSymbol classSymbol, StringBuilder sb)
        {
            string namespaceName = classSymbol.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("// <auto-generated/>");
                sb.AppendLine("namespace " + namespaceName);
                sb.AppendLine("{");
            }
        }

        // Escribe el cierre del namespace en el StringBuilder.
        private static void AppendNamespaceClosing(INamedTypeSymbol classSymbol, StringBuilder sb)
        {
            string namespaceName = classSymbol.ContainingNamespace?.ToDisplayString();
            if (!string.IsNullOrEmpty(namespaceName))
            {
                sb.AppendLine("}");
            }
        }

        // Genera el constructor AllArgs que inicializa todos los miembros.
        private static void GenerateAllArgsConstructor(INamedTypeSymbol classSymbol, List<MemberInfo> members, StringBuilder sb)
        {
            sb.Append("    public ");
            sb.Append(classSymbol.Name);
            sb.Append("(");
            sb.Append(string.Join(", ", members.Select<MemberInfo, string>(m => m.Type.ToDisplayString() + " " + m.Name)));
            sb.AppendLine(")");
            sb.AppendLine("    {");
            foreach (MemberInfo member in members)
            {
                sb.AppendLine("        this." + member.Name + " = " + member.Name + ";");
            }
            sb.AppendLine("    }");
        }

        // Genera la clase Builder con sus métodos Set y Build.
        private static void GenerateBuilderClass(INamedTypeSymbol classSymbol, List<MemberInfo> members, string builderMethodPrefix, StringBuilder sb)
        {
            sb.AppendLine("    public sealed class " + classSymbol.Name + "Builder");
            sb.AppendLine("    {");

            foreach (MemberInfo member in members)
            {
                sb.AppendLine("        private " + member.Type.ToDisplayString() + " " + member.Name + ";");
            }
            sb.AppendLine();

            foreach (MemberInfo member in members)
            {
                string methodName = builderMethodPrefix + char.ToUpper(member.Name[0]) + member.Name.Substring(1);
                sb.AppendLine("        public " + classSymbol.Name + "Builder " + methodName + "(" + member.Type.ToDisplayString() + " value)");
                sb.AppendLine("        {");
                sb.AppendLine("            this." + member.Name + " = value;");
                sb.AppendLine("            return this;");
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            sb.AppendLine("        public " + classSymbol.Name + " Build()");
            sb.AppendLine("        {");
            sb.Append("            return new " + classSymbol.Name + "(");
            sb.Append(string.Join(", ", members.Select<MemberInfo, string>(m => m.Name)));
            sb.AppendLine(");");
            sb.AppendLine("        }");

            sb.AppendLine("    }");
        }

        // Genera el método ToBuilder() que crea un Builder a partir del estado actual.
        private static void GenerateToBuilderMethod(INamedTypeSymbol classSymbol, List<MemberInfo> members, string builderMethodPrefix, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("    public " + classSymbol.Name + "Builder ToBuilder()");
            sb.AppendLine("    {");
            sb.Append("        return new " + classSymbol.Name + "Builder()");
            foreach (MemberInfo member in members)
            {
                string methodName = builderMethodPrefix + char.ToUpper(member.Name[0]) + member.Name.Substring(1);
                sb.AppendLine();
                sb.Append("            ." + methodName + "(this." + member.Name + ")");
            }
            sb.AppendLine(";");
            sb.AppendLine("    }");
        }
    }
}