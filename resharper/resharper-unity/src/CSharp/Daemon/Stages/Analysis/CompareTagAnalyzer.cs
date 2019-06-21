using JetBrains.Annotations;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.Daemon;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Errors;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.Dispatcher;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches;
using JetBrains.ReSharper.Psi.CSharp.Tree;
using JetBrains.ReSharper.Psi.Tree;
using static JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches.UnityProjectSettingsUtils;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.Analysis
{
    [ElementProblemAnalyzer(typeof(IInvocationExpression), HighlightingTypes = new []
    {
        typeof(UnknownTagWarning)
    })]
    public class CompareTagAnalyzer : UnityElementProblemAnalyzer<IInvocationExpression>
    {
        public CompareTagAnalyzer([NotNull] UnityApi unityApi)
            : base(unityApi)
        {
        }

        protected override void Analyze(IInvocationExpression element, ElementProblemAnalyzerData data, IHighlightingConsumer consumer)
        {
            if (IsCompareTagMethod(element))
            {
                var argument = element.ArgumentList.Arguments.FirstOrDefault();
                if (argument == null)
                    return;

                var literal = (argument.Value as ICSharpLiteralExpression)?.ConstantValue.Value as string;
                if (literal == null)
                    return;
                
                var cache = element.GetSolution().TryGetComponent<UnityProjectSettingsCache>();
                if (cache != null && !cache.HasTag(literal)) 
                    consumer.AddHighlighting(new UnknownTagWarning(argument));
            }
        }
    }
}