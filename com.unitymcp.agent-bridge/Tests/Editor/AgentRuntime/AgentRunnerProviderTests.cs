using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityMcp.AgentRuntime;

namespace UnityMcp.AgentBridge.Tests
{
    public sealed class AgentRunnerProviderTests
    {
        private readonly List<AgentRunnerProviderRegistration> _registrations = new List<AgentRunnerProviderRegistration>();
        private string _template;

        [SetUp] public void SetUp() => _template = "fixture-" + Guid.NewGuid().ToString("N");
        [TearDown] public void TearDown() { foreach (var registration in _registrations) registration.Dispose(); _registrations.Clear(); }

        private AgentRunnerProviderRegistration Register(FakeProvider provider)
        {
            var registration = AgentRunnerProviderRegistry.Register(provider);
            _registrations.Add(registration);
            return registration;
        }
        private AgentRunnerPolicy Policy => new AgentRunnerPolicy(_template, 10000, 2, 4096,
            new[] { "project.read" }, new[] { "Assets" }, new[] { "Library" });

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_316.md
        [Test, Category("AGB_316")]
        public void PolicyCopiesCallerCollections()
        {
            var folders = new[] { "Assets" };
            var policy = new AgentRunnerPolicy(_template, 10000, 2, 4096, new[] { "project.read" }, folders, new[] { "Library" });
            folders[0] = "Secrets";
            Assert.That(policy.ReadFolders, Is.EqualTo(new[] { "Assets" }));
            Assert.Throws<NotSupportedException>(() => ((IList<string>)policy.ReadFolders)[0] = "Secrets");
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_317.md
        [Test, Category("AGB_317")]
        public void MissingProviderFailsExplicitly()
        {
            Assert.That(AgentRunnerProviderRegistry.TryResolve(_template, out var registration, out var error), Is.False);
            Assert.That(registration, Is.Null);
            Assert.That(error.Code, Is.EqualTo("AGENT_PROVIDER_UNAVAILABLE"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_318.md
        [Test, Category("AGB_318")]
        public void ConflictingProvidersDoNotSelectAnImplicitWinner()
        {
            Register(new FakeProvider(_template));
            Register(new FakeProvider(_template));
            Assert.That(AgentRunnerProviderRegistry.TryResolve(_template, out _, out var error), Is.False);
            Assert.That(error.Code, Is.EqualTo("AGENT_PROVIDER_AMBIGUOUS"));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_319.md
        [Test, Category("AGB_319")]
        public void PreparedFactoryCapturesPolicyAndProducesIndependentRunners()
        {
            var provider = new FakeProvider(_template) { Output = "first" };
            Register(provider);
            var factory = provider.Prepare(Policy);
            provider.Output = "later";
            var first = (FakeRunner)factory.Create(new AgentRunRequest("fixture"));
            var second = (FakeRunner)factory.Create(new AgentRunRequest("fixture"));
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Output, Is.EqualTo("first"));
            Assert.That(first.Policy.MaxTurns, Is.EqualTo(2));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_320.md
        [Test, Category("AGB_320")]
        public void RetirementRemovesAuthorityBeforeNotifyingTaskOwners()
        {
            var registration = Register(new FakeProvider(_template));
            var notifications = 0;
            Action<AgentRunnerProviderRegistration> retired = owner =>
            {
                if (!ReferenceEquals(owner, registration)) return;
                notifications++;
                Assert.That(owner.IsActive, Is.False);
                Assert.That(AgentRunnerProviderRegistry.TryResolve(_template, out _, out _), Is.False);
            };
            AgentRunnerProviderRegistry.Retired += retired;
            try { registration.Dispose(); registration.Dispose(); }
            finally { AgentRunnerProviderRegistry.Retired -= retired; }
            Assert.That(notifications, Is.EqualTo(1));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_321.md
        [Test, Category("AGB_321")]
        public void ReenabledProviderDoesNotAcquireOldRegistrationIdentity()
        {
            var provider = new FakeProvider(_template);
            var old = Register(provider);
            old.Dispose();
            var current = Register(provider);
            Assert.That(current, Is.Not.SameAs(old));
            old.Dispose();
            Assert.That(AgentRunnerProviderRegistry.TryResolve(_template, out var resolved, out _), Is.True);
            Assert.That(resolved, Is.SameAs(current));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_322.md
        [Test, Category("AGB_322")]
        public void RefreshReusesActiveProviderWithoutCancellation()
        {
            var cache = new AgentPluginLifetimeCache<FakeLifecycle>();
            var provider = new FakeLifecycle();
            for (var i = 0; i < 3; i++)
            {
                cache.BeginRefresh();
                Assert.That(cache.GetOrCreate("package-v1", () => provider), Is.SameAs(provider));
                cache.Activate("package-v1");
                cache.EndRefresh();
            }
            Assert.That(provider.Activations, Is.EqualTo(1));
            Assert.That(provider.Retirements, Is.Zero);
            cache.RetainOnly(Array.Empty<string>());
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_323.md
        [Test, Category("AGB_323")]
        public void ReplacementRetiresOldProviderBeforeNewActivation()
        {
            var cache = new AgentPluginLifetimeCache<FakeLifecycle>();
            var old = cache.GetOrCreate("package-v1", () => new FakeLifecycle());
            cache.Activate("package-v1");
            cache.RetainOnly(new[] { "package-v2" });
            Assert.That(old.Retirements, Is.EqualTo(1));
            var current = cache.GetOrCreate("package-v2", () => new FakeLifecycle());
            cache.Activate("package-v2");
            Assert.That(current, Is.Not.SameAs(old));
            cache.RetainOnly(Array.Empty<string>());
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_324.md
        [Test, Category("AGB_324")]
        public void ReadinessRejectionRetiresOnceAndRetainsConfigurationInstance()
        {
            var cache = new AgentPluginLifetimeCache<FakeLifecycle>();
            var provider = cache.GetOrCreate("package", () => new FakeLifecycle());
            cache.Activate("package");
            cache.Reject("package"); cache.Reject("package");
            Assert.That(provider.Retirements, Is.EqualTo(1));
            Assert.That(cache.Find(item => true), Is.SameAs(provider));
        }

        // TestRecord: Documentation~/AgentBridge/test_records/AGB_325.md
        [Test, Category("AGB_325")]
        public void RetirementFailureDoesNotBlockUnrelatedProviderRemoval()
        {
            var errors = 0;
            var cache = new AgentPluginLifetimeCache<FakeLifecycle>(_ => errors++);
            var bad = cache.GetOrCreate("bad", () => new FakeLifecycle { FailRetirement = true });
            var good = cache.GetOrCreate("good", () => new FakeLifecycle());
            cache.Activate("bad"); cache.Activate("good");
            cache.RetainOnly(Array.Empty<string>());
            Assert.That(errors, Is.EqualTo(1));
            Assert.That(bad.Retirements, Is.EqualTo(1));
            Assert.That(good.Retirements, Is.EqualTo(1));
            Assert.That(cache.Find(_ => true), Is.Null);
        }

        private sealed class FakeProvider : IAgentRunnerProvider
        {
            public FakeProvider(string template) { TemplateIds = new[] { template }; }
            public string Id { get; } = "fixture-" + Guid.NewGuid().ToString("N");
            public IEnumerable<string> TemplateIds { get; }
            public string Output { get; set; }
            public IAgentRunnerFactory Prepare(AgentRunnerPolicy policy) => new FakeFactory(policy, Output);
        }
        private sealed class FakeFactory : IAgentRunnerFactory
        {
            private readonly AgentRunnerPolicy _policy;
            private readonly string _output;
            public FakeFactory(AgentRunnerPolicy policy, string output) { _policy = policy; _output = output; }
            public IAgentRunner Create(AgentRunRequest request) => new FakeRunner { Policy = _policy, Output = _output };
        }
        private sealed class FakeRunner : IAgentRunner
        {
            public AgentRunnerPolicy Policy;
            public string Output;
            public bool CanCancel => true;
            public void Start(AgentRunRequest request, IAgentRunnerContext context) => context.Complete(AgentRunResult.Succeeded(Output));
            public void Advance(IAgentRunnerContext context) { }
            public void RequestCancellation(IAgentRunnerContext context) => context.Cancel();
        }
        private sealed class FakeLifecycle : IAgentPluginLifecycle
        {
            public int Activations;
            public int Retirements;
            public bool FailRetirement;
            public void Activate() => Activations++;
            public void Retire() { Retirements++; if (FailRetirement) throw new InvalidOperationException("fixture"); }
        }
    }
}
