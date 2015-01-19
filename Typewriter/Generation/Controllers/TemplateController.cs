using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnvDTE;
using Typewriter.CodeModel.CodeDom;
using Typewriter.VisualStudio;
using VSLangProj;

namespace Typewriter.Generation.Controllers
{
    public class TemplateController
    {
        private static readonly object locker = new object();

        private readonly DTE dte;
        private readonly IEventQueue eventQueue;

        private bool solutionOpen;
        private ICollection<Template> templates;

        public TemplateController(DTE dte, ISolutionMonitor solutionMonitor, IEventQueue eventQueue)
        {
            this.dte = dte;
            this.eventQueue = eventQueue;

            solutionMonitor.SolutionOpened += (sender, args) => SolutionOpened();
            solutionMonitor.SolutionClosed += (sender, args) => SolutionClosed();
            solutionMonitor.ProjectAdded += (o, e) => ProjectChanged();
            solutionMonitor.ProjectRemoved += (o, e) => ProjectChanged();

            solutionMonitor.FileAdded += (o, e) => FileChanged(e.Path);
            solutionMonitor.FileChanged += (o, e) => FileSaved(e.Path);
            solutionMonitor.FileDeleted += (o, e) => FileChanged(e.Path);
            solutionMonitor.FileRenamed += (o, e) =>
            {
                FileChanged(e.OldPath);
                FileChanged(e.NewPath);
            };
        }

        private void SolutionOpened()
        {
            Log.Debug("Solution Opened");
            solutionOpen = true;
        }

        private void SolutionClosed()
        {
            Log.Debug("Solution Closed");
            solutionOpen = false;
        }

        private void ProjectChanged()
        {
            if (solutionOpen == false) return;
            this.templates = null;
        }

        private bool FileChanged(string path)
        {
            if (path.EndsWith(Constants.Extension, StringComparison.InvariantCultureIgnoreCase))
            {
                this.templates = null;
                return true;
            }

            return false;
        }

        private void FileSaved(string path)
        {
            if (!FileChanged(path)) return;

            var projectItem = dte.Solution.FindProjectItem(path);
            var template = new Template(projectItem);

            foreach (var item in GetReferencedProjectItems(projectItem, ".cs"))
            {
                eventQueue.QueueRender(item.FileNames[1], s =>
                {
                    try
                    {
                        var stopwatch = Stopwatch.StartNew();
                        Log.Debug("Render {0}", s);

                        var file = new FileInfo(dte.Solution.FindProjectItem(s));
                        template.Render(file);

                        stopwatch.Stop();
                        Log.Debug("Render completed in {0} ms", stopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception exception)
                    {
                        Log.Error("Render Exception: {0}, {1}", exception.Message, exception.StackTrace);
                    }
                });
            }
        }

        public ICollection<Template> Templates
        {
            get { return LoadTemplates(); }
        }

        private ICollection<Template> LoadTemplates()
        {
            var stopwatch = Stopwatch.StartNew();

            lock (locker)
            {
                if (this.templates == null)
                {
                    var items = GetProjectItems(Constants.Extension);
                    this.templates = items.Select(i => new Template(i)).ToList();
                }
                else
                {
                    foreach (var template in this.templates)
                    {
                        try
                        {
                            template.VerifyProjectItem();
                        }
                        catch
                        {
                            Log.Debug("Invalid template");
                            this.templates = null;

                            return LoadTemplates();
                        }
                    }
                }
            }

            stopwatch.Stop();
            Log.Debug("Templates loaded in {0} ms", stopwatch.ElapsedMilliseconds);

            return this.templates;
        }

        private IEnumerable<ProjectItem> GetReferencedProjectItems(ProjectItem i, string extension)
        {
            var vsproject = i.ContainingProject.Object as VSProject;
            if (vsproject != null)
            {
                foreach (Reference reference in vsproject.References)
                {
                    var sp = reference.SourceProject;
                    if (sp != null)
                    {
                        foreach (var item in sp.AllProjectItems().Where(item => item.Name.EndsWith(extension, StringComparison.InvariantCultureIgnoreCase)))
                        {
                            yield return item;
                        }
                    }
                }

                foreach (var item in i.ContainingProject.AllProjectItems().Where(item => item.Name.EndsWith(extension, StringComparison.InvariantCultureIgnoreCase)))
                {
                    yield return item;
                }
            }
        }

        private IEnumerable<ProjectItem> GetProjectItems(string extension)
        {
            return dte.Solution.AllProjetcs().SelectMany(p => p.AllProjectItems())
                .Where(i => i.Name.EndsWith(extension, StringComparison.InvariantCultureIgnoreCase));
        }
    }
}
