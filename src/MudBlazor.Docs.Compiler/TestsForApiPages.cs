using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Components;

namespace MudBlazor.Docs.Compiler
{
    public class TestsForApiPages
    {
        public bool Execute()
        {
            var paths = new Paths();
            bool success = true;
            try
            {    
                Directory.CreateDirectory(paths.TestDirPath);
                
                using (var f = File.Create(paths.ApiPageTestsFilePath))
                using (var w = new StreamWriter(f) { NewLine = "\n" })
                {
                    w.WriteLine("// NOTE: this file is autogenerated. Any changes will be overwritten!");
                    w.WriteLine(
                        @"using Microsoft.AspNetCore.Components;
    using Microsoft.Extensions.DependencyInjection;
    using NUnit.Framework;
    using MudBlazor.UnitTests.Mocks;
    using MudBlazor.Docs.Examples;
    using MudBlazor.Dialog;
    using MudBlazor.Services;
    using MudBlazor.Docs.Components;
    using Bunit.Rendering;
    using System;
    using Toolbelt.Blazor.HeadElement;
    using MudBlazor.UnitTests;
    using MudBlazor.Charts;
    using Bunit;

    #if NET5_0
    using ComponentParameter = Bunit.ComponentParameter;
    #endif

    namespace MudBlazor.UnitTests.Components
    {
        [TestFixture]
        public class _AllApiPages
        {
            // These tests just check if all the API pages to see if they throw any exceptions

    ");
                    var mudBlazorComponents = typeof(MudAlert).Assembly.GetTypes().OrderBy(t => t.FullName).Where(t => t.IsSubclassOf(typeof(ComponentBase)));
                    foreach (var type in mudBlazorComponents)
                    {
                        if (type.IsAbstract)
                            continue;
                        if (type.Name.Contains("Base"))
                            continue;
                        if (type.Namespace.Contains("InternalComponents"))
                            continue;
                        w.WriteLine(
                            @$"
            [Test]
            public void {SafeTypeName(type, removeT:true)}_API_Test()
            {{
                    using var ctx = new Bunit.TestContext();
                    ctx.Services.AddSingleton<NavigationManager>(new MockNavigationManager());
                    ctx.Services.AddSingleton<IDialogService>(new DialogService());
                    ctx.Services.AddSingleton<IResizeListenerService>(new MockResizeListenerService());
                    ctx.Services.AddSingleton<IHeadElementHelper>(new MockHeadElementHelper());
                    ctx.Services.AddSingleton<ISnackbar>(new MockSnackbar());
                    var comp = ctx.RenderComponent<DocsApi>(ComponentParameter.CreateParameter(""Type"", typeof({SafeTypeName(type)})));
                    Console.WriteLine(comp.Markup);
            }}
    ");
                    }

                    w.WriteLine(
                        @"    }
    }
    ");
                    w.Flush();
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Error generating {paths.ApiPageTestsFilePath} : {e.Message}");
                success = false;
            }

            return success;
        }

        private static string SafeTypeName(Type type, bool removeT=false)
        {
            if (!type.IsGenericType)
                return type.Name;
           var genericTypename= type.Name;
            if (removeT)
                return genericTypename.Replace("`1", "");
            return genericTypename.Replace("`1", "<T>"); ;
        }
    }
}