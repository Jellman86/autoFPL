using AutoFpl.Api.Sources;

namespace AutoFpl.Api.Intelligence;

public sealed class ResearchSourceRefreshSignal
{
    private readonly bool _alignWithOfficialCapture;
    private int _requested;

    public ResearchSourceRefreshSignal(
        OfficialFplPollingOptions officialOptions,
        ResearchSourcePollingOptions researchOptions)
    {
        ArgumentNullException.ThrowIfNull(officialOptions);
        ArgumentNullException.ThrowIfNull(researchOptions);
        _alignWithOfficialCapture =
            officialOptions.Interval is TimeSpan officialInterval
            && researchOptions.Interval is TimeSpan researchInterval
            && officialInterval == researchInterval;
    }

    public bool Enabled => _alignWithOfficialCapture;

    public void RequestAfterOfficialCapture()
    {
        if (_alignWithOfficialCapture)
        {
            Interlocked.Exchange(ref _requested, 1);
        }
    }

    internal bool ConsumeRequest() =>
        Interlocked.Exchange(ref _requested, 0) == 1;
}
