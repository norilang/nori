using NUnit.Framework;
using Nori.Compiler;
using System.IO;

namespace Nori.Lsp.Tests
{
    [TestFixture]
    public class SampleCompileTests
    {
        private static readonly string SamplesDir = Path.GetFullPath(
            Path.Combine(TestContext.CurrentContext.TestDirectory,
            "..", "..", "..", "..", "..", "Samples"));

        private void CompileSample(string relativePath)
        {
            string fullPath = Path.Combine(SamplesDir, relativePath);
            if (!File.Exists(fullPath))
            {
                Assert.Ignore($"Sample not found: {fullPath}");
                return;
            }
            string source = File.ReadAllText(fullPath);
            string fileName = Path.GetFileName(fullPath);
            var result = NoriCompiler.Compile(source, fileName);
            Assert.That(result.Success, Is.True,
                $"Compilation failed for {relativePath}:\n{DiagnosticPrinter.FormatAll(result.Diagnostics)}");
        }

        [Test] public void Hello() => CompileSample("basic scene/NoriProgramSources/hello.nori");
        [Test] public void Spinner() => CompileSample("basic scene/NoriProgramSources/spinner.nori");
        [Test] public void Lobby() => CompileSample("basic scene/NoriProgramSources/lobby.nori");
        [Test] public void PickupToy() => CompileSample("basic scene/NoriProgramSources/pickup_toy.nori");
        [Test] public void Follower() => CompileSample("basic scene/NoriProgramSources/follower.nori");
        [Test] public void Lights() => CompileSample("basic scene/NoriProgramSources/lights.nori");
        [Test] public void SyncedToggle() => CompileSample("basic scene/NoriProgramSources/synced_toggle.nori");
        [Test] public void QuizGame() => CompileSample("basic scene/NoriProgramSources/quiz_game.nori");
        [Test] public void Scoreboard() => CompileSample("basic scene/NoriProgramSources/scoreboard.nori");
        [Test] public void Door() => CompileSample("basic scene/NoriProgramSources/door.nori");
        [Test] public void WorldSettings() => CompileSample("world_settings.nori");

        [Test] public void Empty() => CompileSample("NoriExampleScene/NoriProgramSources/empty.nori");
        [Test] public void ToggleGameObject() => CompileSample("NoriExampleScene/NoriProgramSources/toggle_game_object.nori");
        [Test] public void SendEventOnInteract() => CompileSample("NoriExampleScene/NoriProgramSources/send_event_on_interact.nori");
        [Test] public void UseStationOnInteract() => CompileSample("NoriExampleScene/NoriProgramSources/use_station_on_interact.nori");
        [Test] public void FireOnTrigger() => CompileSample("NoriExampleScene/NoriProgramSources/fire_on_trigger.nori");
        [Test] public void SetActiveFromPlayerTrigger() => CompileSample("NoriExampleScene/NoriProgramSources/set_active_from_player_trigger.nori");
        [Test] public void VrcWorldSettings() => CompileSample("NoriExampleScene/NoriProgramSources/vrc_world_settings.nori");
        [Test] public void FollowPlayer() => CompileSample("NoriExampleScene/NoriProgramSources/follow_player.nori");
        [Test] public void GetPlayersText() => CompileSample("NoriExampleScene/NoriProgramSources/get_players_text.nori");
        [Test] public void SetAllPlayersMaxAudioDistance() => CompileSample("NoriExampleScene/NoriProgramSources/set_all_players_max_audio_distance.nori");
        [Test] public void IsValid() => CompileSample("NoriExampleScene/NoriProgramSources/is_valid.nori");
        [Test] public void AvatarPedestalProgram() => CompileSample("NoriExampleScene/NoriProgramSources/avatar_pedestal_program.nori");
        [Test] public void AvatarScalingSettings() => CompileSample("NoriExampleScene/NoriProgramSources/avatar_scaling_settings.nori");
        [Test] public void SendEventOnTimer() => CompileSample("NoriExampleScene/NoriProgramSources/send_event_on_timer.nori");
        [Test] public void SendEventOnMouseDown() => CompileSample("NoriExampleScene/NoriProgramSources/send_event_on_mouse_down.nori");
        [Test] public void PlayerTrigger() => CompileSample("NoriExampleScene/NoriProgramSources/player_trigger.nori");
        [Test] public void PlayerCollisionParticles() => CompileSample("NoriExampleScene/NoriProgramSources/player_collision_particles.nori");
        [Test] public void Projectile() => CompileSample("NoriExampleScene/NoriProgramSources/projectile.nori");
        [Test] public void SimpleForLoop() => CompileSample("NoriExampleScene/NoriProgramSources/simple_for_loop.nori");
        [Test] public void ToggleSync() => CompileSample("NoriExampleScene/NoriProgramSources/toggle_sync.nori");
        [Test] public void SliderSync() => CompileSample("NoriExampleScene/NoriProgramSources/slider_sync.nori");
        [Test] public void DropdownSync() => CompileSample("NoriExampleScene/NoriProgramSources/dropdown_sync.nori");
        [Test] public void ButtonSyncAnyone() => CompileSample("NoriExampleScene/NoriProgramSources/button_sync_anyone.nori");
        [Test] public void ButtonSyncOwner() => CompileSample("NoriExampleScene/NoriProgramSources/button_sync_owner.nori");
        [Test] public void ButtonSyncBecomeOwner() => CompileSample("NoriExampleScene/NoriProgramSources/button_sync_become_owner.nori");
        [Test] public void SyncValueTypes() => CompileSample("NoriExampleScene/NoriProgramSources/sync_value_types.nori");
        [Test] public void SyncValueTypesLinear() => CompileSample("NoriExampleScene/NoriProgramSources/sync_value_types_linear.nori");
        [Test] public void SyncValueTypesSmooth() => CompileSample("NoriExampleScene/NoriProgramSources/sync_value_types_smooth.nori");
        [Test] public void Chooser() => CompileSample("NoriExampleScene/NoriProgramSources/chooser.nori");
        [Test] public void CubeArraySync() => CompileSample("NoriExampleScene/NoriProgramSources/cube_array_sync.nori");
        [Test] public void SyncPickupColor() => CompileSample("NoriExampleScene/NoriProgramSources/sync_pickup_color.nori");
        [Test] public void UdonSyncPlayer() => CompileSample("NoriExampleScene/NoriProgramSources/udon_sync_player.nori");
        [Test] public void ObjectPool() => CompileSample("NoriExampleScene/NoriProgramSources/object_pool.nori");
        [Test] public void PooledBox() => CompileSample("NoriExampleScene/NoriProgramSources/pooled_box.nori");
        [Test] public void PenLine() => CompileSample("NoriExampleScene/NoriProgramSources/pen_line.nori");
        [Test] public void SimplePen() => CompileSample("NoriExampleScene/NoriProgramSources/simple_pen.nori");
        [Test] public void DownloadString() => CompileSample("NoriExampleScene/NoriProgramSources/download_string.nori");
        [Test] public void ImageDownload() => CompileSample("NoriExampleScene/NoriProgramSources/image_download.nori");
        [Test] public void ChangeMaterialOnEvent() => CompileSample("NoriExampleScene/NoriProgramSources/change_material_on_event.nori");
        [Test] public void PickupAndUse() => CompileSample("NoriExampleScene/NoriProgramSources/pickup_and_use.nori");
    }
}
