namespace CodeSage.Api.Dtos;

public record RepoActivityDto(
    string Name, string FullName, string? Language, int Stars,
    string Url, DateTime? UpdatedAt, int[] Weeks, int Commits);

public record LanguageSliceDto(string Name, int Pct);

public record OverviewTotalsDto(int Repos, int Languages, int CommitsThisWeek);

public record OverviewDto(
    List<RepoActivityDto> Repos,
    List<LanguageSliceDto> Languages,
    OverviewTotalsDto Totals);