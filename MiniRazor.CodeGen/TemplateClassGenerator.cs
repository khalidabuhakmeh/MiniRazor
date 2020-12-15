﻿using System.Text;
using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.CodeAnalysis;
using MiniRazor.CodeGen.Internal.Extensions;

namespace MiniRazor.CodeGen
{
    [Generator]
    public partial class TemplateClassGenerator : ISourceGenerator
    {
        private static readonly string GeneratorVersion =
            typeof(TemplateClassGenerator).Assembly.GetName().Version.ToString(3);

        private void ProcessFile(GeneratorExecutionContext context, string filePath, string content)
        {
            // Generate class name from file name
            var className = SanitizeIdentifier(Path.GetFileNameWithoutExtension(filePath));

            var code = Razor.ToCSharp(content, options =>
            {
                options.ConfigureClass((_, node) =>
                {
                    node.ClassName = className;
                });
            });

            // Get model type from the template's base class
            var modelTypeName =
                Regex.Match(code, @"MiniRazor\.TemplateBase<(.+)>").Groups[1].Value.NullIfWhiteSpace() ??
                "dynamic"; // fallback to dynamic (shouldn't normally happen)

            // Extend the template with some additional code
            code = code.Insert(code.IndexOf("public async override", StringComparison.Ordinal), $@"
/// <summary>Renders the template using the specified writer.</summary>
public static async global::System.Threading.Tasks.Task RenderAsync(global::System.IO.TextWriter output, {modelTypeName} model)
{{
    var template = new {className}();
    template.Output = output;
    template.Model = model;

    await template.ExecuteAsync().ConfigureAwait(false);
}}

/// <summary>Renders the template to a string.</summary>
public static async global::System.Threading.Tasks.Task<string> RenderAsync({modelTypeName} model)
{{
    using (var output = new global::System.IO.StringWriter())
    {{
        await RenderAsync(output, model).ConfigureAwait(false);
        return output.ToString();
    }}
}}
");

            // Add documentation to the class
            code = code.Insert(code.IndexOf("internal partial class", StringComparison.Ordinal), $@"
/// <summary>Template: {filePath}</summary>
/// <remarks>Generated by MiniRazor v{GeneratorVersion} on {DateTimeOffset.Now}.</remarks>
");

            // Disable nullability checks on the entire file
            code = code
                .Replace("#nullable restore", "")
                .Replace("#nullable disable", "")
                .Insert(0, "#nullable disable" + Environment.NewLine);

            context.AddSource(className, code);
        }

        /// <inheritdoc />
        public void Execute(GeneratorExecutionContext context)
        {
            foreach (var file in context.AdditionalFiles)
            {
                var isRazorTemplate = string.Equals(
                    Path.GetExtension(file.Path),
                    ".cshtml",
                    StringComparison.OrdinalIgnoreCase
                );

                if (!isRazorTemplate)
                    continue;

                var content = file.GetText(context.CancellationToken)?.ToString();

                if (string.IsNullOrWhiteSpace(content))
                    continue;

                ProcessFile(context, file.Path, content!);
            }
            
            context.AddSource("Foo", "namespace MiniRazor.CodeGen.Demo { public class Maarten { } }");
        }

        /// <inheritdoc />
        public void Initialize(GeneratorInitializationContext context) { }
    }

    public partial class TemplateClassGenerator
    {
        private static string SanitizeIdentifier(string symbolName)
        {
            var buffer = new StringBuilder(symbolName);

            // Must start with a letter or an underscore
            if (buffer.Length > 0 && buffer[0] != '_' && !char.IsLetter(buffer[0]))
            {
                buffer.Insert(0, '_');
            }

            // Replace all other prohibited characters with underscores
            for (var i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != '_' && !char.IsLetterOrDigit(buffer[i]))
                    buffer[i] = '_';
            }

            return buffer.ToString();
        }
    }
}