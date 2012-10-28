﻿using System.Collections.Generic;
using System.Linq;
using Microsoft.Ajax.Utilities;

namespace Cassette.RequireJS
{
    class PlainScript : Module, IAssetTransformer
    {
        readonly IAmdConfiguration modules;
        readonly SimpleJsonSerializer jsonSerializer;

        public PlainScript(IAsset asset, Bundle bundle, IAmdConfiguration modules) 
            : base(asset, bundle)
        {
            this.modules = modules;
            jsonSerializer = new SimpleJsonSerializer();
            asset.AddAssetTransformer(this);
        }

        public string ModuleReturnExpression { get; set; }

        public string Transform(string source, IAsset asset)
        {
            if (ModuleReturnExpression != null)
            {
                return ModuleWithReturn(source, ModuleReturnExpression);
            }
            
            if (TopLevelVariableFinder.JavaScriptContainsTopLevelVariable(source, Alias))
            {
                return ModuleWithReturn(source, Alias);
            }
            
            return ModuleWithoutReturn(source);
        }

        IEnumerable<string> DependencyPaths
        {
            get { return Asset.References.Select(r => r.ToPath).Select(p => modules[p].ModulePath); }
        }

        IEnumerable<string> DependencyAliases
        {
            get { return Asset.References.Select(r => r.ToPath).Select(p => modules[p].Alias); }
        }

        class TopLevelVariableFinder : TreeVisitor
        {
            public static bool JavaScriptContainsTopLevelVariable(string javaScriptSource, string variableName)
            {
                var parser = new JSParser(javaScriptSource);
                var tree = parser.Parse(new CodeSettings());
                var finder = new TopLevelVariableFinder(variableName);
                tree.Accept(finder);
                return finder.found;
            }

            readonly string varName;
            bool found;

            TopLevelVariableFinder(string varName)
            {
                this.varName = varName;
            }

            public override void Visit(VariableDeclaration node)
            {
                if (node.EnclosingScope is GlobalScope && node.Identifier == varName)
                {
                    found = true;
                }

                base.Visit(node);
            }
        }

        string ModuleWithoutReturn(string source)
        {
            return string.Format(
                "define({0},{1},function({2}){{{3}\r\n}});",
                jsonSerializer.Serialize(ModulePath),
                jsonSerializer.Serialize(DependencyPaths),
                string.Join(",", DependencyAliases),
                source
            );
        }

        string ModuleWithReturn(string source, string export)
        {
            return string.Format(
                "define({0},{1},function({2}){{{3}\r\nreturn {4};}});",
                jsonSerializer.Serialize(ModulePath),
                jsonSerializer.Serialize(DependencyPaths),
                string.Join(",", DependencyAliases),
                source,
                export
            );
        }
    }
}