using AutoFpl.Contracts.Intelligence;

namespace AutoFpl.Api.Intelligence;

public sealed record FbrefReviewedPlayerIdentity(
    string SourcePlayerId,
    string SourcePlayerName,
    string SourceTeamId,
    string TeamName,
    int OfficialPlayerCode);

public static class FbrefPlayerIdentityBridge
{
    public const string Version = "fbref-official-player-bridge/v1";
    public const long ReviewedSnapshotId = 27;
    public const string ReviewedContentSha256 =
        "0c80ce784b02cbcb21692859effc969109c6e54376e9f511ad3b6b49b32fa55b";

    private static readonly IReadOnlyList<FbrefReviewedPlayerIdentity> Entries =
    [
        new("2944f86f", "Frank Onyeka", "f7e3dfe9", "Coventry City", 428580),
        new("0118dd71", "Ben Wilson", "f7e3dfe9", "Coventry City", 110690),
        new("6a471ee8", "Bobby Thomas", "f7e3dfe9", "Coventry City", 461102),
        new("23aa33e2", "Liam Kitching", "f7e3dfe9", "Coventry City", 436761),
        new("58448fbb", "Milan van Ewijk", "f7e3dfe9", "Coventry City", 451284),
        new("2a158ded", "Jay Dasilva", "f7e3dfe9", "Coventry City", 173878),
        new("9b066938", "Kaine Kesler-Hayden", "f7e3dfe9", "Coventry City", 465390),
        new("676b6c9b", "Jake Bidwell", "f7e3dfe9", "Coventry City", 80178),
        new("a4183553", "Joël Latibeaudière", "f7e3dfe9", "Coventry City", 204813),
        new("e28e9bec", "Luke Woolfenden", "f7e3dfe9", "Coventry City", 220583),
        new("4d395e2b", "Jack Rudoni", "f7e3dfe9", "Coventry City", 480457),
        new("81a487b9", "Matt Grimes", "f7e3dfe9", "Coventry City", 168144),
        new("5e67ea3a", "Tatsuhiro Sakamoto", "f7e3dfe9", "Coventry City", 501459),
        new("0d5a541f", "Ephron Mason-Clark", "f7e3dfe9", "Coventry City", 241293),
        new("9722d36c", "Josh Eccles", "f7e3dfe9", "Coventry City", 439668),
        new("e8cc2cbb", "Victor Torp", "f7e3dfe9", "Coventry City", 226965),
        new("152402c4", "George Shepherd", "f7e3dfe9", "Coventry City", 684268),
        new("76c1ea7c", "Raphael Borges Rodrigues", "f7e3dfe9", "Coventry City", 492498),
        new("91cd37b7", "Haji Wright", "f7e3dfe9", "Coventry City", 176412),
        new("fb152f96", "Brandon Thomas-Asante", "f7e3dfe9", "Coventry City", 230730),
        new("26d3b5be", "Ellis Simms", "f7e3dfe9", "Coventry City", 218997),
        new("165c7444", "Jahnoah Markelo", "f7e3dfe9", "Coventry City", 551262),
        new("d0330b1d", "Dillon Phillips", "bd8769d1", "Hull City", 167825),
        new("debe38a0", "John Egan", "bd8769d1", "Hull City", 108416),
        new("29c29cef", "Charlie Hughes", "bd8769d1", "Hull City", 501902),
        new("d6192210", "Semi Ajayi", "bd8769d1", "Hull City", 146426),
        new("97aba136", "Lewie Coyle", "bd8769d1", "Hull City", 193278),
        new("e0d1510a", "Cody Drameh", "bd8769d1", "Hull City", 433590),
        new("73b1d65b", "Ryan Giles", "bd8769d1", "Hull City", 232351),
        new("2875c756", "Matty Jacob", "bd8769d1", "Hull City", 449428),
        new("377105d7", "Cathal McCarthy", "bd8769d1", "Hull City", 661800),
        new("8ac454b5", "Paddy McNair", "bd8769d1", "Hull City", 160817),
        new("983515f8", "Liam Millar", "bd8769d1", "Hull City", 232905),
        new("2f6ec02a", "Kieran Dowell", "bd8769d1", "Hull City", 171270),
        new("43f5d4e6", "Matt Crooks", "bd8769d1", "Hull City", 101282),
        new("ba4d4908", "Regan Slater", "bd8769d1", "Hull City", 243413),
        new("15944afd", "Eliot Matazo", "bd8769d1", "Hull City", 465525),
        new("a6fbfa77", "Abu Kamara", "bd8769d1", "Hull City", 490161),
        new("56d838f7", "Darko Gyabi", "bd8769d1", "Hull City", 470294),
        new("4fe1f01d", "Enis Destan", "bd8769d1", "Hull City", 500957),
        new("c666574f", "Christian Walton", "b74092de", "Ipswich Town", 108813),
        new("6ea97fd5", "Alex Palmer", "b74092de", "Ipswich Town", 112520),
        new("e608ed4c", "David Button", "b74092de", "Ipswich Town", 50093),
        new("9f30e7ed", "Cédric Kipré", "b74092de", "Ipswich Town", 183015),
        new("1d042188", "Dara O'Shea", "b74092de", "Ipswich Town", 216616),
        new("8574c61f", "Leif Davis", "b74092de", "Ipswich Town", 455084),
        new("1f169636", "Jacob Greaves", "b74092de", "Ipswich Town", 449429),
        new("9319781b", "Ben Johnson", "b74092de", "Ipswich Town", 222018),
        new("3ec9195b", "Darnell Furlong", "b74092de", "Ipswich Town", 176420),
        new("b6d1cf5c", "Marcelino Núñez", "b74092de", "Ipswich Town", 501390),
        new("a17a94d6", "Azor Matusiwa", "b74092de", "Ipswich Town", 241117),
        new("b3fe23c6", "Wes Burns", "b74092de", "Ipswich Town", 149929),
        new("48eed8d5", "Jack Taylor", "b74092de", "Ipswich Town", 231899),
        new("e16932d8", "Jack Clarke", "b74092de", "Ipswich Town", 443261),
        new("788c0277", "Chiedozie Ogbene", "b74092de", "Ipswich Town", 229164),
        new("78e87179", "George Hirst", "b74092de", "Ipswich Town", 222625),
        new("e2b384d2", "Chuba Akpom", "b74092de", "Ipswich Town", 147675),
        new("8450467d", "Ali Al Hamadi", "b74092de", "Ipswich Town", 462387),
        new("b2c66859", "Kasey McAteer", "b74092de", "Ipswich Town", 461587),
        new("48a08f79", "Anis Mehmeti", "b74092de", "Ipswich Town", 232457),
    ];

    private static readonly IReadOnlyDictionary<
        (string SourcePlayerId, string SourceTeamId),
        FbrefReviewedPlayerIdentity> BySourceIdentity = Entries.ToDictionary(
            entry => (entry.SourcePlayerId, entry.SourceTeamId));

    static FbrefPlayerIdentityBridge()
    {
        if (Entries.Count != 60
            || Entries.Select(entry => entry.OfficialPlayerCode).Distinct().Count()
                != Entries.Count
            || BySourceIdentity.Count != Entries.Count)
        {
            throw new InvalidOperationException(
                "The FBref reviewed identity bridge must contain 60 unique mappings.");
        }
    }

    public static IReadOnlyList<FbrefReviewedPlayerIdentity> All => Entries;

    public static bool AppliesTo(ResearchSourceSnapshotDocument snapshot) =>
        snapshot.SnapshotId == ReviewedSnapshotId
        && StringComparer.Ordinal.Equals(
            snapshot.ContentSha256,
            ReviewedContentSha256);

    public static FbrefReviewedPlayerIdentity? Find(
        string sourcePlayerId,
        string sourceTeamId) =>
        BySourceIdentity.GetValueOrDefault((sourcePlayerId, sourceTeamId));
}
