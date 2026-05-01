namespace ProctorLti.Api.Models;

public record LaunchBoot(
    string? TestRunnerUrl,
    string? UserName,
    string? DeploymentId,
    string ProctorRoomId,
    string ControlChannel = "d2l-lti-test-runner-control");
