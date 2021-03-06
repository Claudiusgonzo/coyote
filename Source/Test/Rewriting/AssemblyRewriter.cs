﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Coyote.IO;
using Microsoft.Coyote.SystematicTesting.Interception;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Microsoft.Coyote.Rewriting
{
    /// <summary>
    /// Rewrites an assembly for systematic testing.
    /// </summary>
    internal class AssemblyRewriter
    {
        /// <summary>
        /// Temporary directory that is used to write the rewritten assemblies
        /// in the case that they are replacing the original ones.
        /// </summary>
        /// <remarks>
        /// We need this because it seems Mono.Cecil does not allow to rewrite in-place.
        /// </remarks>
        private const string TempDirectory = "__temp_coyote__";

        /// <summary>
        /// Configuration for rewriting assemblies.
        /// </summary>
        private readonly Configuration Configuration;

        /// <summary>
        /// List of assemblies that are not allowed to be rewritten.
        /// </summary>
        private readonly List<string> DisallowedAssemblies;

        /// <summary>
        /// List of transforms we are applying while rewriting.
        /// </summary>
        private readonly List<AssemblyTransform> Transforms;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssemblyRewriter"/> class.
        /// </summary>
        /// <param name="configuration">The configuration for this rewriter.</param>
        private AssemblyRewriter(Configuration configuration)
        {
            this.Configuration = configuration;
            this.DisallowedAssemblies = new List<string>()
            {
                "Microsoft.Coyote.dll",
                "Microsoft.Coyote.Test.dll",
                "System.Private.CoreLib.dll",
                "mscorlib.dll"
            };

            this.Transforms = new List<AssemblyTransform>()
            {
                 new TaskTransform(),
                 new MonitorTransform()
            };
        }

        /// <summary>
        /// Rewrites the assemblies specified in the configuration.
        /// </summary>
        internal static void Rewrite(Configuration configuration)
        {
            var binaryRewriter = new AssemblyRewriter(configuration);
            binaryRewriter.Rewrite();
        }

        /// <summary>
        /// Performs the assembly rewriting.
        /// </summary>
        private void Rewrite()
        {
            // Create the output directory and copy any necessery files.
            string outputDirectory = this.CreateOutputDirectoryAndCopyFiles();

            // Rewrite the assembly files to the output directory.
            foreach (string assemblyPath in this.Configuration.AssemblyPaths)
            {
                this.RewriteAssembly(assemblyPath, outputDirectory);
            }

            if (this.Configuration.IsReplacingAssemblies)
            {
                // If we are replacing the original assemblies, then delete the temporary output directory.
                Directory.Delete(outputDirectory, true);
            }
        }

        /// <summary>
        /// Rewrites the specified assembly definition.
        /// </summary>
        private void RewriteAssembly(string assemblyPath, string outputDirectory)
        {
            var isSymbolFileAvailable = IsSymbolFileAvailable(assemblyPath);
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, new ReaderParameters()
            {
                AssemblyResolver = this.GetAssemblyResolver(),
                ReadSymbols = isSymbolFileAvailable
            });

            string assemblyName = Path.GetFileName(assemblyPath);
            if (this.DisallowedAssemblies.Contains(assemblyName))
            {
                throw new InvalidOperationException($"Rewriting the '{assemblyName}' assembly ({assembly.FullName}) is not allowed.");
            }

            Console.WriteLine($"... Rewriting the '{assemblyName}' assembly ({assembly.FullName})");
            foreach (var transform in this.Transforms)
            {
                // Traverse the assembly to apply each transformation pass.
                Debug.WriteLine($"..... Applying the '{transform.GetType().Name}' transform");
                foreach (var module in assembly.Modules)
                {
                    RewriteModule(module, transform);
                }
            }

            // Write the binary in the output path with portable symbols enabled.
            string outputPath = Path.Combine(outputDirectory, assemblyName);
            Console.WriteLine($"... Writing the modified '{assemblyName}' assembly to " +
                $"{(this.Configuration.IsReplacingAssemblies ? assemblyPath : outputPath)}");
            assembly.Write(outputPath, new WriterParameters()
            {
                WriteSymbols = isSymbolFileAvailable,
                SymbolWriterProvider = new PortablePdbWriterProvider()
            });

            assembly.Dispose();
            if (this.Configuration.IsReplacingAssemblies)
            {
                File.Copy(outputPath, assemblyPath, true);
                if (isSymbolFileAvailable)
                {
                    string pdbFile = Path.ChangeExtension(outputPath, "pdb");
                    string targetPdbFile = Path.ChangeExtension(assemblyPath, "pdb");
                    File.Copy(pdbFile, targetPdbFile, true);
                }
            }
        }

        /// <summary>
        /// Rewrites the specified module definition using the specified transform.
        /// </summary>
        private static void RewriteModule(ModuleDefinition module, AssemblyTransform transform)
        {
            Debug.WriteLine($"....... Module: {module.Name} ({module.FileName})");
            transform.VisitModule(module);
            foreach (var type in module.GetTypes())
            {
                RewriteType(type, transform);
            }
        }

        /// <summary>
        /// Rewrites the specified type definition using the specified transform.
        /// </summary>
        private static void RewriteType(TypeDefinition type, AssemblyTransform transform)
        {
            Debug.WriteLine($"......... Type: {type.FullName}");
            transform.VisitType(type);
            foreach (var field in type.Fields)
            {
                Debug.WriteLine($"........... Field: {field.FullName}");
                transform.VisitField(field);
            }

            foreach (var method in type.Methods)
            {
                RewriteMethod(method, transform);
            }
        }

        /// <summary>
        /// Rewrites the specified method definition using the specified transform.
        /// </summary>
        private static void RewriteMethod(MethodDefinition method, AssemblyTransform transform)
        {
            Debug.WriteLine($"........... Method {method.FullName}");
            transform.VisitMethod(method);

            // Only non-abstract method bodies can be rewritten.
            if (!method.IsAbstract)
            {
                foreach (var variable in method.Body.Variables)
                {
                    transform.VisitVariable(variable);
                }

                // Do exception handlers before the method instructions because they are a
                // higher level concept and it's handy to pre-process them before seeing the
                // raw instructions.
                if (method.Body.HasExceptionHandlers)
                {
                    foreach (var handler in method.Body.ExceptionHandlers)
                    {
                        transform.VisitExceptionHandler(handler);
                    }
                }

                // Rewrite the method body instructions.
                Instruction instruction = method.Body.Instructions.FirstOrDefault();
                while (instruction != null)
                {
                    instruction = transform.VisitInstruction(instruction);
                    instruction = instruction.Next;
                }
            }
        }

        /// <summary>
        /// Creates the output directory, if it does not already exists, and copies all necessery files.
        /// </summary>
        /// <returns>The output directory path.</returns>
        private string CreateOutputDirectoryAndCopyFiles()
        {
            string sourceDirectory = this.Configuration.AssembliesDirectory;
            string outputDirectory = Directory.CreateDirectory(this.Configuration.IsReplacingAssemblies ?
                Path.Combine(this.Configuration.OutputDirectory, TempDirectory) : this.Configuration.OutputDirectory).FullName;

            if (!this.Configuration.IsReplacingAssemblies)
            {
                Console.WriteLine($"... Copying all files to the '{outputDirectory}' directory");

                // Copy all files to the output directory, while preserving directory structure.
                foreach (string directoryPath in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    // Avoid copying the output directory itself.
                    if (!directoryPath.StartsWith(outputDirectory))
                    {
                        Debug.WriteLine($"..... Copying the '{directoryPath}' directory");
                        Directory.CreateDirectory(Path.Combine(outputDirectory, directoryPath.Substring(sourceDirectory.Length + 1)));
                    }
                }

                foreach (string filePath in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                {
                    // Avoid copying any files from the output directory.
                    if (!filePath.StartsWith(outputDirectory))
                    {
                        Debug.WriteLine($"..... Copying the '{filePath}' file");
                        File.Copy(filePath, Path.Combine(outputDirectory, filePath.Substring(sourceDirectory.Length + 1)), true);
                    }
                }
            }

            // Copy the `Microsoft.Coyote.dll` assembly to the output directory.
            string coyoteAssemblyPath = typeof(ControlledTask).Assembly.Location;
            File.Copy(coyoteAssemblyPath, Path.Combine(this.Configuration.OutputDirectory, Path.GetFileName(coyoteAssemblyPath)), true);

            return outputDirectory;
        }

        /// <summary>
        /// Returns a new assembly resolver.
        /// </summary>
        private IAssemblyResolver GetAssemblyResolver()
        {
            // TODO: can we reuse it, or do we need a new one for each assembly?
            var assemblyResolver = new DefaultAssemblyResolver();

            // Add known search directories for resolving assemblies.
            assemblyResolver.AddSearchDirectory(Path.GetDirectoryName(typeof(ControlledTask).Assembly.Location));
            assemblyResolver.AddSearchDirectory(this.Configuration.AssembliesDirectory);

            // Add the assembly resolution error handler.
            assemblyResolver.ResolveFailure += OnResolveAssemblyFailure;
            return assemblyResolver;
        }

        /// <summary>
        /// Checks if the symbol file for the specified assembly is available.
        /// </summary>
        private static bool IsSymbolFileAvailable(string assemblyPath) =>
            File.Exists(Path.ChangeExtension(assemblyPath, "pdb"));

        /// <summary>
        /// Handles an assembly resolution error.
        /// </summary>
        private static AssemblyDefinition OnResolveAssemblyFailure(object sender, AssemblyNameReference reference)
        {
            Console.WriteLine("Error resolving assembly: " + reference.FullName);
            return null;
        }
    }
}
