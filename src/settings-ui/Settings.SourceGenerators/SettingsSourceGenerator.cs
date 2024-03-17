﻿// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;

namespace Settings.SourceGenerators
{
    [Generator]
    public class SettingsSourceGenerator : ISourceGenerator
    {
        private GeneratorExecutionContext _context;
        private string[] _modules = new[] { "Hosts", "FileLocksmith" };
        private StringBuilder _source;
        private string _projectPath;
        private bool _failed = false;

        public void Execute(GeneratorExecutionContext context)
        {
            // Remove following comment for debugging
#if DEBUG
            if (!Debugger.IsAttached)
            {
                Debugger.Launch();
            }
#endif

            _context = context;

            if (!_context.AnalyzerConfigOptions.GlobalOptions.TryGetValue("build_property.ProjectPath", out _projectPath) || _projectPath is null)
            {
                return;
            }

            _source = new StringBuilder(@"// <auto-generated />
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Settings.Ui.VNext.Helpers;

namespace Settings.Ui.VNext
{
    public static class GeneratedFunctions
    {
");

            GeneratePopulateNavigationItemsFunction();
            if (_failed)
            {
                return;
            }

            GenerateSettingsPages();

            _source.Append(@"       }
    }
}");
            _context.AddSource("GeneratedFunctions.g.cs", _source.ToString());
        }

        private void GeneratePopulateNavigationItemsFunction()
        {
            _source.Append(@"       public static void PopulateNavigationItems(NavigationView navigationView)
        {");

            foreach (var item in _modules)
            {
                XmlDocument doc = new XmlDocument();
                doc.Load($"{_projectPath}/ConfigFiles/{item}.xml");
                doc.Schemas.Add("http://schemas.microsoft.com/PowerToys/FileActionsMenu/ModuleDefinition", $"{_projectPath}/ConfigFiles/ModuleDefinition.xsd");
                doc.Validate((_, e) =>
                {
                    _context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("PT0001", "XML Validation error", $"XML Validation error in {item}.xml: " + e.Message, "XML validation Error", DiagnosticSeverity.Error, true, "Error: " + e.Message), null));
                    _failed = true;
                });

                if (_failed)
                {
                    return;
                }

                string moduleName = doc.GetNode("ModuleSettings").Attributes["Name"].Value;
                string iconUri = doc.GetNode("ModuleSettings").Attributes["Icon"].Value;

                // Todo: Add NavHelper.NavigateTo property
                _source.Append(
                    $@"
                    {{
                        NavigationViewItem navigationViewItem = new();
                        var loader = ResourceLoaderInstance.ResourceLoader;
                        navigationViewItem.Content = loader.GetString(""Shell_{moduleName}/Content"");
                        navigationViewItem.Icon = new BitmapIcon()  {{ UriSource = new Uri(""{iconUri}""), ShowAsMonochrome = false }};
                        navigationView.MenuItems.Add(navigationViewItem);
                        NavHelper.SetNavigateTo(navigationViewItem, typeof({moduleName}Page));
                    }}
");
            }
        }

        private void GenerateSettingsPages()
        {
            foreach (string item in _modules)
            {
                StringBuilder settingsPage = new StringBuilder();

                XmlDocument doc = new XmlDocument();
                doc.Load($"{_projectPath}/ConfigFiles/{item}.xml");

                string moduleName = doc.GetNode("ModuleSettings").Attributes["Name"].Value;
                string headerImage = doc.GetNode("ModuleSettings/Header").Attributes["Image"].Value;
                string headerPrimaryLinkName = doc.GetNode("ModuleSettings/Header/PrimaryLink").Attributes["Name"].Value;
                string headerPrimaryLinkUri = doc.GetNode("ModuleSettings/Header/PrimaryLink").Attributes["Uri"].Value;

                // Generate secondary links
                StringBuilder secondaryLinksSource = new StringBuilder();
                XmlNodeList secondaryLinks = doc.GetNodes("ModuleSettings/Footer/SecondaryLink");

                foreach (XmlNode link in secondaryLinks)
                {
                    string linkName = link.Attributes["Name"].Value;
                    string linkUri = link.Attributes["Uri"].Value;

                    secondaryLinksSource.Append(
                        $@"
                    new Settings.Ui.VNext.Controls.PageLink
                    {{
                        Text = loader.GetString(""{linkName}/Text""),
                        Link = new Uri(""{linkUri}"")
                    }},
");
                }

                doc.GetNode("ModuleSettings/Header").RemoveAll();
                doc.GetNode("ModuleSettings/Footer").RemoveAll();

                StringBuilder content = new StringBuilder();
                GenerateSettingsPageElements(doc.GetNode("ModuleSettings").ChildNodes, content);

                settingsPage.Append($@"// <auto-generated />
using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Settings.Ui.VNext.Helpers;
using Settings.Ui.VNext.Controls;
using System.Collections.Generic;

namespace Settings.Ui.VNext
{{
    public sealed partial class {moduleName}Page : Page {{
        public {moduleName}Page()
        {{
            this.InitializeComponent();

            var loader = ResourceLoaderInstance.ResourceLoader;

            this.Content = new Settings.Ui.VNext.Controls.SettingsPageControl {{
                ModuleImageSource = ""{headerImage}"",
                ModuleTitle = loader.GetString(""{moduleName}/ModuleTitle""),
                ModuleDescription = loader.GetString(""{moduleName}/ModuleDescription""),
                PrimaryLinks = new System.Collections.ObjectModel.ObservableCollection<Settings.Ui.VNext.Controls.PageLink> {{
                    new Settings.Ui.VNext.Controls.PageLink
                    {{
                        Text = loader.GetString(""{headerPrimaryLinkName}/Text""),
                        Link = new Uri(""{headerPrimaryLinkUri}"")
                    }}
                }},
                {(secondaryLinks.Count > 0 ? $@"SecondaryLinksHeader = loader.GetString(""{moduleName}/SecondaryLinksHeader"")," : string.Empty)}
                SecondaryLinks = new System.Collections.ObjectModel.ObservableCollection<Settings.Ui.VNext.Controls.PageLink>
                {{
                    {secondaryLinksSource}
                }},
                ModuleContent =
                    new StackPanel
                    {{
                        Children =
                        {{
                            {content}
                        }}
                    }},
            }};
        }}
    }}
}}");

                _context.AddSource($"{moduleName}Page.g.cs", settingsPage.ToString());
                GenerateSettingsPageXaml(moduleName);
            }
        }

        private void GenerateSettingsPageXaml(string moduleName)
        {
#pragma warning disable RS1035 // Do not use APIs banned for analyzers
            if (!Directory.Exists($"{_projectPath}/Generated Files"))
            {
                Directory.CreateDirectory($"{_projectPath}/Generated Files");
            }

            string content = $@"<!-- <auto-generated /> -->
<Page
    x:Class=""Settings.Ui.VNext.{moduleName}Page""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
    xmlns:controls=""using:Microsoft.PowerToys.Settings.UI.Controls""
    xmlns:d=""http://schemas.microsoft.com/expression/blend/2008""
    xmlns:mc=""http://schemas.openxmlformats.org/markup-compatibility/2006""
    xmlns:tkcontrols=""using:CommunityToolkit.WinUI.Controls""
    xmlns:ui=""using:CommunityToolkit.WinUI""
    mc:Ignorable=""d"">
</Page>";

            File.WriteAllText($"{_projectPath}/Generated Files/{moduleName}Page.g.xaml", content);
#pragma warning restore RS1035 // Do not use APIs banned for analyzers
        }

        public void GenerateSettingsPageElements(XmlNodeList elements, StringBuilder source)
        {
            foreach (XmlNode element in elements)
            {
                switch (element.Name)
                {
                    case "Group":
                        GenerateGroup(element, source);
                        break;
                    case "InfoBar":
                        GenerateInfoBar(element, source);
                        break;
                }
            }
        }

        public void GenerateGroup(XmlNode element, StringBuilder source)
        {
            string name = element.Attributes["Name"].Value;

            StringBuilder content = new StringBuilder();
            GenerateSettingsPageElements(element.ChildNodes, content);

            source.Append(
                $@"
                    new SettingsGroup
                    {{
                        Header = loader.GetString(""{name}/Header""),
                        ItemsSource = new System.Collections.ObjectModel.ObservableCollection<UIElement>
                        {{
                            {content}
                        }}
                    }},
                ");
        }

        public void GenerateInfoBar(XmlNode element, StringBuilder source)
        {
            string severity = element.Attributes["Severity"].Value;
            string message = element.Attributes["Text"].Value;

            source.Append(
                $@"
                    new InfoBar
                    {{
                        Severity = InfoBarSeverity.{severity},
                        Message = loader.GetString(""{message}/Title""),
                        IsOpen = true,
                        IsClosable = false,
                        IsTabStop = true,
                    }},
                ");
        }

        public void Initialize(GeneratorInitializationContext context)
        {
        }
    }
}