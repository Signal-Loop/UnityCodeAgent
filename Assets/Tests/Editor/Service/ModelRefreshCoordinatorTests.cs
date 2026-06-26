using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SignalLoop.UnityCodeAgent.Contracts;
using SignalLoop.UnityCodeAgent.Settings;
using UnityEditor;

namespace SignalLoop.UnityCodeAgent.Service
{
    public sealed class ModelRefreshCoordinatorTests
    {
        [Test]
        [Description("Goal: verify model refresh starts asynchronously and guards duplicate refreshes. Scope: settings model refresh coordinator only. Boundaries: excludes live service startup and HTTP model discovery.")]
        public void StartRefresh_ReturnsBeforeModelTaskCompletesAndGuardsDuplicates()
        {
            var modelTask = new TaskCompletionSource<IReadOnlyList<ModelInfoDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var applyCount = 0;
            var coordinator = CreateCoordinator(
                (_, _) => modelTask.Task,
                _ => applyCount++);

            var started = coordinator.StartRefresh();
            var duplicateStarted = coordinator.StartRefresh();

            Assert.That(started, Is.True);
            Assert.That(duplicateStarted, Is.False);
            Assert.That(coordinator.IsRefreshInProgress, Is.True);
            Assert.That(coordinator.RefreshTask.IsCompleted, Is.False);
            Assert.That(applyCount, Is.EqualTo(0));
            Assert.That(coordinator.Message, Is.EqualTo("Refreshing models..."));

            coordinator.Dispose();
        }

        [Test]
        [Description("Goal: verify AgentService-style progress messages update the settings model info message before refresh completion. Scope: settings refresh progress routing only. Boundaries: excludes the real AgentService implementation.")]
        public void StartRefresh_UpdatesProgressMessageBeforeCompletion()
        {
            var modelTask = new TaskCompletionSource<IReadOnlyList<ModelInfoDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var coordinator = CreateCoordinator(
                (progress, _) =>
                {
                    progress("Starting agent service...");
                    return modelTask.Task;
                },
                _ => { });

            coordinator.StartRefresh();

            Assert.That(coordinator.IsRefreshInProgress, Is.True);
            Assert.That(coordinator.Message, Is.EqualTo("Starting agent service..."));
            Assert.That(coordinator.MessageType, Is.EqualTo(MessageType.Info));

            coordinator.Dispose();
        }

        [Test]
        [Description("Goal: verify successful model refresh applies returned models and clears in-progress state. Scope: settings model refresh coordinator only. Boundaries: excludes asset persistence and dropdown rendering.")]
        public async Task RefreshTask_OnSuccessAppliesModelsAndReportsLoadedCount()
        {
            IReadOnlyList<ModelInfoDto> appliedModels = null;
            var models = new[]
            {
                new ModelInfoDto("gpt-5-mini", "GPT-5 Mini"),
                new ModelInfoDto("gpt-5", "GPT-5")
            };
            var coordinator = CreateCoordinator(
                (_, _) => Task.FromResult<IReadOnlyList<ModelInfoDto>>(models),
                result => appliedModels = result);

            coordinator.StartRefresh();
            await coordinator.RefreshTask;

            Assert.That(appliedModels, Is.SameAs(models));
            Assert.That(coordinator.IsRefreshInProgress, Is.False);
            Assert.That(coordinator.Message, Is.EqualTo("Loaded 2 model(s) from the server."));
            Assert.That(coordinator.MessageType, Is.EqualTo(MessageType.Info));
        }

        [Test]
        [Description("Goal: verify failed model refresh reports the error in the same model info message surface. Scope: settings model refresh coordinator only. Boundaries: excludes production logging and transport errors.")]
        public async Task RefreshTask_OnFailureReportsErrorAndClearsInProgress()
        {
            var coordinator = CreateCoordinator(
                (_, _) => Task.FromException<IReadOnlyList<ModelInfoDto>>(new InvalidOperationException("model endpoint failed")),
                _ => Assert.Fail("Models should not be applied after refresh failure."));

            coordinator.StartRefresh();
            await coordinator.RefreshTask;

            Assert.That(coordinator.IsRefreshInProgress, Is.False);
            Assert.That(coordinator.Message, Is.EqualTo("model endpoint failed"));
            Assert.That(coordinator.MessageType, Is.EqualTo(MessageType.Error));
        }

        [Test]
        [Description("Goal: verify disposing the refresh coordinator cancels the pending model load and does not apply late results. Scope: settings refresh disposal behavior only. Boundaries: excludes Unity editor lifecycle dispatch.")]
        public async Task Dispose_CancelsPendingRefreshAndIgnoresLateCompletion()
        {
            var modelTask = new TaskCompletionSource<IReadOnlyList<ModelInfoDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var applyCount = 0;
            CancellationToken observedToken = CancellationToken.None;
            var coordinator = CreateCoordinator(
                (_, cancellationToken) =>
                {
                    observedToken = cancellationToken;
                    return modelTask.Task;
                },
                _ => applyCount++);

            coordinator.StartRefresh();
            coordinator.Dispose();
            modelTask.SetResult(new[] { new ModelInfoDto("gpt-5", "GPT-5") });
            await coordinator.RefreshTask;

            Assert.That(observedToken.IsCancellationRequested, Is.True);
            Assert.That(applyCount, Is.EqualTo(0));
        }

        private static ModelRefreshCoordinator CreateCoordinator(
            Func<Action<string>, CancellationToken, Task<IReadOnlyList<ModelInfoDto>>> loadModelsAsync,
            Action<IReadOnlyList<ModelInfoDto>> applyModels)
        {
            return new ModelRefreshCoordinator(
                loadModelsAsync,
                applyModels,
                (action, _) =>
                {
                    action();
                    return Task.CompletedTask;
                },
                () => { });
        }
    }
}
