using SonicDesktopRelay.Signaling;
using Xunit;

namespace SonicDesktopRelay.Presentation.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void An_idle_app_can_start_either_role()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(SessionSnapshot.Idle);

        Assert.True(viewModel.CanShare);
        Assert.True(viewModel.CanWatch);
    }

    [Fact]
    public void While_sharing_neither_role_can_be_started_again()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Sharing, "AB12CD",
            Guid.NewGuid(), 2, SignalingState.Connected, null));

        Assert.False(viewModel.CanShare);
        Assert.False(viewModel.CanWatch);
    }

    [Fact]
    public void While_busy_neither_role_can_be_started()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Joining, null, null, 0,
            SignalingState.Connecting, null));

        Assert.False(viewModel.CanShare);
        Assert.False(viewModel.CanWatch);
    }

    [Fact]
    public void A_failure_is_reported_in_words_rather_than_as_an_error_code()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "device_type_not_allowed"));

        Assert.Equal("This session only accepts Windows computers running SonicDesktopRelay.",
            viewModel.StatusText);
    }

    [Fact]
    public void An_invalid_code_is_reported_without_hinting_at_which_part_was_wrong()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "invalid_code"));

        Assert.Equal("That code is not valid, or the session has ended.", viewModel.StatusText);
    }

    [Fact]
    public void A_media_failure_points_at_Diagnostics_where_the_reason_actually_is()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "media_unavailable"));

        Assert.Equal("Screen capture or the video encoder could not start. See Diagnostics.",
            viewModel.StatusText);
    }

    [Fact]
    public void An_unrecognised_error_code_still_produces_a_usable_message()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Failed, null, null, 0,
            SignalingState.Disconnected, "something_new"));

        Assert.Equal("Something went wrong. Try again.", viewModel.StatusText);
    }

    [Fact]
    public void Sharing_reports_the_viewer_count()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Apply(new SessionSnapshot(SessionPhase.Sharing, "AB12CD",
            Guid.NewGuid(), 2, SignalingState.Connected, null));

        Assert.Equal("Sharing — 2 watching", viewModel.StatusText);
    }
}
