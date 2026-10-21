using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Wyvernland
{
    /// <summary>Manual traces from the upright 1368 x 1824 reference photograph.
    /// Faint pencil lines are interpreted; these are not surveyed coordinates.</summary>
    public sealed class ProceduralMap : MonoBehaviour
    {

        private sealed class Contour
        {
            public readonly float Elevation;
            public readonly int Parent;
            public readonly Vector2[] Points;
            public Contour(float elevation, int parent, string coordinates)
            {
                Elevation = elevation;
                Parent = parent;
                string[] pairs = coordinates.Split(' ');
                Points = new Vector2[pairs.Length];
                for (int i = 0; i < pairs.Length; i++)
                {
                    string[] xy = pairs[i].Split(',');
                    float px = float.Parse(xy[0], CultureInfo.InvariantCulture);
                    float py = float.Parse(xy[1], CultureInfo.InvariantCulture);
                    Points[i] = new Vector2((px - 40f) / 1210f * WorldConfig.MapWidth,
                        (1660f - py) / 1510f * WorldConfig.MapLength);
                }
            }
            public float SignedDistance(Vector2 p)
            {
                bool inside = false;
                float squaredDistance = float.PositiveInfinity;
                for (int i = 0, j = Points.Length - 1; i < Points.Length; j = i++)
                {
                    Vector2 a = Points[j], b = Points[i];
                    Vector2 edge = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / Mathf.Max(edge.sqrMagnitude, 0.0001f));
                    squaredDistance = Mathf.Min(squaredDistance, (p - a - edge * t).sqrMagnitude);
                    if ((a.y > p.y) != (b.y > p.y) &&
                        p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                return Mathf.Sqrt(squaredDistance) * (inside ? 1f : -1f);
            }
        }
        private static readonly Contour[] Contours =
        {
            // Main island
            new(0f, -1, "77,700 130,700 157,687 179,705 242,710 284,697 318,698 355,685 371,672 365,646 383,632 398,615 402,590 388,574 369,568 354,580 326,562 315,534 301,517 298,505 321,498 329,489 353,502 377,492 395,475 389,453 408,431 424,409 440,417 455,430 480,421 505,398 526,390 553,404 575,427 590,421 635,387 668,376 665,384 641,390 619,420 605,454 598,490 584,520 592,540 590,577 601,625 606,650 625,676 630,689 653,711 675,733 712,749 746,763 779,772 817,779 863,775 904,759 942,742 971,732 981,713 1010,698 1032,678 1054,655 1058,628 1071,604 1080,572 1080,531 1085,515 1080,469 1092,484 1100,513 1123,529 1133,532 1135,548 1128,558 1141,581 1159,593 1165,613 1187,632 1213,658 1217,674 1200,693 1194,733 1185,750 1171,757 1176,779 1166,799 1170,812 1198,821 1192,848 1200,870 1193,895 1201,921 1201,933 1211,942 1197,951 1192,983 1204,1015 1195,1031 1204,1042 1194,1067 1194,1101 1179,1105 1173,1129 1157,1145 1142,1178 1133,1174 1136,1159 1124,1153 1112,1159 1092,1158 1084,1175 1071,1190 1065,1208 1048,1216 1038,1214 1015,1229 997,1227 1000,1206 981,1203 973,1192 967,1161 949,1152 932,1132 923,1105 900,1097 881,1096 875,1070 866,1046 843,1040 823,1023 820,1030 836,1045 854,1053 852,1067 869,1096 887,1112 908,1120 906,1134 924,1155 931,1176 944,1200 963,1219 973,1240 974,1265 965,1283 969,1296 949,1308 928,1311 911,1326 915,1342 903,1365 899,1391 882,1400 870,1415 842,1429 817,1448 797,1442 777,1465 763,1472 759,1499 752,1473 747,1434 735,1410 732,1387 724,1373 724,1351 707,1338 706,1320 718,1285 725,1250 735,1212 744,1192 749,1170 737,1155 727,1155 719,1177 706,1188 695,1218 670,1231 648,1240 640,1238 640,1259 646,1280 640,1294 650,1303 633,1314 642,1327 627,1345 616,1346 615,1360 607,1365 600,1385 592,1384 592,1375 575,1376 554,1367 541,1376 526,1372 493,1380 479,1393 474,1407 459,1422 440,1396 437,1379 429,1359 413,1354 400,1343 383,1345 367,1338 346,1341 330,1349 318,1338 302,1357 280,1366 268,1390 247,1397 238,1405 222,1396 201,1398 180,1409 163,1407 144,1418 123,1415 132,1390 151,1376 172,1354 190,1321 199,1288 213,1254 221,1226 211,1209 212,1190 198,1172 200,1154 190,1137 198,1125 210,1120 223,1093 229,1060 224,1033 230,1026 214,1000 211,966 204,956 180,947 164,928 144,938 127,929 107,934 93,928 91,915 83,905 94,896 89,881 96,841 128,814 154,802 171,780 151,775 135,784 121,778 111,792 97,787 93,773 83,770 84,758 74,752 89,737 88,715"),
            // Northern island
            new(0f, -1, "765,310 778,294 766,294 783,278 807,263 832,252 854,247 879,245 899,223 912,204 925,197 929,185 948,181 964,185 970,197 970,209 977,218 970,229 985,235 988,253 1000,264 1025,281 1043,294 1045,302 1058,312 1068,328 1065,342 1054,350 1044,350 1035,361 1016,363 1005,350 1005,340 992,341 982,334 978,322 958,316 935,310 915,294 902,299 881,302 858,293 843,292 840,284 821,294 800,298 782,307"),
            // Southern island
            new(0f, -1, "353,1518 366,1510 371,1490 382,1477 384,1462 401,1451 415,1454 432,1446 440,1435 456,1442 464,1437 487,1455 500,1471 523,1477 533,1487 554,1479 575,1468 598,1466 623,1470 629,1477 620,1490 614,1507 619,1515 613,1537 606,1552 595,1567 574,1575 558,1588 544,1591 533,1604 516,1609 506,1620 495,1606 491,1595 476,1610 454,1620 438,1627 433,1609 421,1597 425,1587 400,1582 394,1574 386,1576 380,1555 371,1535 358,1530"),
            // Northwest ridge
            new(100f, 0, "339,731 361,714 379,700 386,674 398,650 410,622 420,594 409,570 386,554 371,537 376,529 402,518 418,499 432,481 428,461 441,446 465,442 491,431 510,416 529,399 536,424 559,435 575,437 577,456 560,480 546,501 554,524 550,547 537,570 532,589 539,609 557,623 575,630 577,641 560,657 568,664 541,681 519,704 499,720 484,718 470,710 448,714 432,707 412,713 382,727 359,741"),
            // Northwest inner ridge
            new(200f, 3, "404,552 425,530 451,507 459,490 459,474 480,457 505,445 522,455 525,480 523,494 531,510 535,518 521,525 505,534 490,548 486,569 480,581 488,597 502,607 510,625 499,646 479,652 467,664 460,680 444,689 426,682 412,685 406,674 411,659 425,645 429,616 443,593 446,575 438,556"),
            // Northwest summit
            new(300f, 4, "445,536 461,526 474,521 477,513 469,500 473,486 485,470 499,467 503,473 497,487 501,501 496,513 486,525 481,538 461,544 447,546"),
            // Northwest south summit
            new(300f, 4, "434,655 446,639 450,625 451,608 459,602 470,609 471,624 479,633 464,643 450,647 443,656"),
            // Western hill
            new(100f, 0, "175,742 200,740 220,730 246,730 254,743 271,748 280,765 292,780 301,797 300,827 287,847 275,861 279,874 264,896 242,901 226,907 214,900 192,906 178,889 159,892 154,877 156,858 168,842 190,818 206,793 215,776 211,763 195,755 175,755"),
            // Western summit
            new(200f, 7, "183,852 202,833 219,808 242,798 254,778 267,776 278,794 275,811 266,834 263,851 247,867 237,882 220,879 207,864 201,850"),
            // Central western hill
            new(100f, 0, "339,960 367,937 396,912 408,896 403,864 410,840 399,819 402,801 417,787 451,783 485,790 524,788 568,796 607,800 637,810 650,827 655,851 661,865 651,891 629,906 602,916 572,916 547,923 537,947 509,969 478,991 447,996 415,990 380,1000 357,987"),
            // Central western inner
            new(200f, 9, "400,952 427,931 447,912 453,896 444,876 437,866 447,850 451,829 466,821 501,821 548,829 586,823 608,824 620,835 621,846 600,857 570,867 540,879 531,902 511,921 480,937 445,946 418,965"),
            // Central western summit
            new(300f, 10, "475,895 482,877 487,868 488,850 500,844 526,846 534,856 529,866 511,873 505,891 493,903 481,909"),
            // Diagonal ridge
            new(100f, 0, "533,1033 545,1006 559,979 579,955 619,947 650,935 668,941 702,921 735,899 758,876 781,874 803,883 821,888 829,896 817,908 808,912 805,942 806,969 785,983 759,990 720,1009 698,1036 685,1060 669,1073 652,1060 620,1066 585,1067 558,1072 542,1060 529,1050"),
            // Diagonal summit
            new(200f, 12, "578,1022 594,997 618,981 656,972 682,961 713,939 742,917 770,903 785,902 778,916 760,929 756,943 735,957 704,978 673,993 636,1005 611,1020 593,1033 581,1038"),
            // Southwest massif
            new(100f, 0, "208,1361 237,1325 260,1292 254,1254 255,1215 249,1181 250,1154 271,1126 270,1090 267,1057 275,1032 302,1030 323,1041 357,1036 387,1041 410,1048 451,1037 482,1023 499,1029 482,1050 471,1070 461,1082 463,1103 473,1133 474,1167 470,1195 477,1225 480,1251 437,1254 426,1274 425,1294 416,1316 404,1310 391,1301 365,1301 337,1293 309,1311 280,1332 251,1341 221,1364"),
            // Southwest inner
            new(200f, 14, "284,1292 296,1273 296,1245 291,1214 292,1180 307,1149 312,1127 312,1084 338,1080 367,1079 397,1071 423,1062 438,1065 423,1086 430,1111 435,1150 435,1180 442,1212 433,1236 415,1242 406,1259 394,1266 378,1257 350,1258 324,1271 297,1287"),
            // Southwest summit
            new(300f, 15, "344,1235 358,1219 343,1204 335,1187 340,1165 335,1146 344,1120 365,1103 389,1095 398,1102 394,1128 389,1150 391,1173 402,1196 399,1214 376,1228 360,1230"),
            // Eastern massif
            new(100f, 0, "856,952 869,937 880,916 901,906 922,907 951,913 977,909 1005,901 1034,898 1049,879 1061,858 1065,828 1079,814 1092,825 1108,851 1113,880 1127,901 1140,932 1152,953 1159,979 1167,1000 1160,1021 1152,1041 1135,1044 1127,1066 1124,1091 1112,1112 1094,1136 1060,1151 1041,1175 1016,1166 1006,1145 1005,1115 985,1094 963,1084 947,1072 935,1049 929,1024 925,1003 917,989 893,985 886,966"),
            // Eastern inner
            new(200f, 17, "917,950 941,943 975,940 1014,932 1038,918 1055,896 1075,883 1090,900 1103,931 1117,951 1127,978 1127,1001 1114,1022 1105,1047 1091,1061 1075,1084 1057,1113 1043,1126 1029,1125 1020,1108 1025,1084 1019,1066 1003,1055 990,1035 986,1012 972,994 948,984 936,978 937,966"),
            // Eastern summit
            new(300f, 18, "975,969 1000,965 1032,960 1055,946 1074,927 1082,947 1087,963 1093,982 1084,1001 1083,1025 1070,1050 1057,1069 1046,1050 1041,1028 1027,1004 1009,988 980,979"),
            // Northeast ridge
            new(100f, 0, "1000,748 1027,733 1057,711 1074,684 1074,661 1090,641 1105,613 1100,596 1107,584 1121,596 1134,628 1162,649 1162,675 1170,688 1162,708 1146,726 1120,739 1115,746 1090,753 1062,755 1045,762 1023,760 1014,770 1006,767 1010,756"),
            // Northeast summit
            new(200f, 20, "1051,728 1075,714 1096,696 1102,675 1112,648 1119,646 1125,659 1119,675 1128,693 1120,702 1104,712 1091,721 1069,722"),
            // Southeast peninsula
            new(100f, 0, "778,1175 780,1145 790,1127 811,1117 833,1119 850,1131 862,1153 876,1172 894,1194 917,1210 932,1239 941,1264 937,1274 914,1281 891,1300 875,1329 865,1365 847,1386 823,1397 792,1405 776,1400 772,1365 761,1336 758,1313 767,1294 764,1275 770,1250 775,1216"),
            // Southeast summit
            new(200f, 22, "812,1356 819,1327 817,1299 813,1281 812,1256 819,1237 822,1212 836,1205 848,1208 850,1229 865,1241 870,1256 868,1279 852,1301 837,1327 823,1347"),
            // Northern inner
            new(100f, 1, "876,269 895,262 912,247 918,227 931,216 949,218 960,237 965,253 981,275 1008,290 1023,304 1018,315 1005,303 978,293 954,281 930,276 912,269 891,275 878,275"),
            // Northern summit
            new(200f, 24, "926,259 932,245 934,230 942,231 949,247 952,263 939,264"),
            // Southern inner
            new(100f, 2, "398,1524 402,1506 416,1489 442,1480 461,1478 474,1490 457,1506 461,1522 472,1548 468,1569 451,1562 436,1559 428,1541 412,1535"),
            // Southern west summit
            new(200f, 26, "418,1520 420,1506 431,1500 441,1504 434,1517 442,1533 434,1537 425,1526"),
            // Southern east hill
            new(100f, 2, "477,1524 482,1507 508,1511 533,1510 554,1497 576,1487 591,1489 598,1500 588,1514 580,1517 582,1531 572,1547 549,1560 536,1577 517,1573 493,1577 500,1558 493,1543"),
            // Southern east summit
            new(200f, 28, "509,1546 514,1530 531,1523 542,1526 558,1515 562,1521 550,1534 546,1550 531,1555 515,1551"),
        };

        // Plains traced from the red annotations in 3qr3d.png. They reuse the
        // photograph's coordinate system so they stay aligned with the coastline.
        private static readonly Contour[] PlainAreas =
        {
            // Northern basin: narrow in the west and broadening around the bay.
            new(0f, -1, "565,430 601,429 618,464 603,520 603,605 631,667 684,718 760,749 837,767 908,752 971,718 1018,692 1040,708 1036,744 989,780 921,809 842,823 767,813 697,786 641,751 596,704 562,650 544,600 545,520"),
            // Western shelf and its southward arm.
            new(0f, -1, "302,746 389,733 478,711 559,716 613,739 628,764 608,792 542,796 475,781 409,786 379,810 374,874 348,943 331,1009 302,1030 257,1024 227,997 222,951 242,910 263,861 276,805"),
            // Small plain west of the eastern massif.
            new(0f, -1, "884,818 923,800 970,785 1012,779 1033,793 1040,818 1026,844 991,863 944,878 897,889 867,881 854,860 859,840"),
            // Broad southern-central plain, tapering northeast.
            new(0f, -1, "774,980 809,982 826,1007 816,1048 790,1091 757,1130 716,1171 680,1222 656,1288 646,1341 621,1362 573,1367 520,1362 480,1346 470,1318 479,1266 491,1195 506,1138 535,1107 583,1095 637,1083 684,1054 731,1015"),
        };

        private readonly struct RiverbedPoint
        {
            public readonly Vector2 Position;
            public readonly float Width;
            public readonly float Depth;
            public RiverbedPoint(float photoX,float photoY,float width,float depth)
            {
                Position=new Vector2((photoX-40f)/1210f*WorldConfig.MapWidth,
                    (1660f-photoY)/1510f*WorldConfig.MapLength);
                Width=width;
                Depth=depth;
            }
        }

        // Centre lines traced from the blue marks in cvbsfbfs.png. Width and
        // depth vary deterministically along each bed within the requested
        // 8–20 m and 2–4 m ranges.
        private static readonly RiverbedPoint[][] AuthoredRiverbeds =
        {
            new[]
            {
                new RiverbedPoint(297,735,12,2.5f), new RiverbedPoint(370,744,18,3.2f),
                new RiverbedPoint(478,745,10,2.3f), new RiverbedPoint(525,752,16,3.7f),
                new RiverbedPoint(562,720,20,3.4f), new RiverbedPoint(586,675,11,2.2f),
                new RiverbedPoint(596,648,15,3.0f)
            },
            new[]
            {
                new RiverbedPoint(220,991,17,3.1f), new RiverbedPoint(309,1003,9,2.2f),
                new RiverbedPoint(405,1010,14,3.8f), new RiverbedPoint(467,990,20,3.4f),
                new RiverbedPoint(520,950,11,2.6f), new RiverbedPoint(585,918,16,3.3f),
                new RiverbedPoint(640,884,8,2.1f), new RiverbedPoint(690,834,13,2.9f),
                new RiverbedPoint(741,794,18,3.6f)
            },
            new[]
            {
                new RiverbedPoint(529,1375,10,2.4f), new RiverbedPoint(507,1320,18,3.5f),
                new RiverbedPoint(489,1250,13,3.0f), new RiverbedPoint(486,1170,20,3.9f),
                new RiverbedPoint(493,1090,9,2.2f), new RiverbedPoint(508,1025,15,3.2f)
            },
            new[]
            {
                new RiverbedPoint(976,740,14,2.7f), new RiverbedPoint(1019,758,20,3.8f),
                new RiverbedPoint(1078,767,11,2.4f), new RiverbedPoint(1120,797,17,3.3f),
                new RiverbedPoint(1145,858,8,2.1f), new RiverbedPoint(1152,929,13,2.8f),
                new RiverbedPoint(1168,1016,19,3.7f)
            }
        };
        private static readonly RiverbedPoint[][] Riverbeds=BuildAdjustedRiverbeds();

        private readonly System.Collections.Generic.HashSet<MeshCollider> surfaces = new();
        private readonly System.Collections.Generic.List<Mesh> ownedMeshes = new();
        private Material clay;
        private StoneTerrainCutout stoneCutouts;
        public int LastStoneCutoutSurfaceCount=>stoneCutouts?.LastRebuiltSurfaceCount??0;
        public void BeginStonePitGeneration() => stoneCutouts?.BeginBatch();
        public bool TryGetOriginalStoneSurface(float x,float z,out RaycastHit hit)
        {
            hit=default;
            return stoneCutouts!=null&&stoneCutouts.OriginalSurface(x,z,out hit);
        }
        public void FinishStonePitGeneration() { stoneCutouts?.EndBatch(); Physics.SyncTransforms(); }
        internal System.Collections.IEnumerator PrepareStonePitSurfacesIncrementally() =>
            stoneCutouts.PrepareSurfacesIncrementally();
        internal System.Collections.IEnumerator FinishStonePitGenerationIncrementally()
        {
            var finish=stoneCutouts.EndBatchIncrementally();
            while(finish.MoveNext()) yield return finish.Current;
            Physics.SyncTransforms();
        }
        public void CarveStoneTerrain(Mesh mesh,Matrix4x4 stoneToWorld) =>
            stoneCutouts?.Subtract(mesh,stoneToWorld);
        public void WriteBrowserPitTiles(System.IO.Stream destination) => stoneCutouts.WriteBrowserPitTiles(destination);
        internal System.Collections.IEnumerator LoadBrowserPitTilesIncrementally()
        {
            var asset=Resources.Load<TextAsset>("Terrain/BrowserPitTiles");
            if(asset==null) throw new System.InvalidOperationException("Browser pit tiles are missing. Rebuild the browser game.");
            byte[] bytes=asset.bytes;
            Resources.UnloadAsset(asset);
            using var stream=new System.IO.MemoryStream(bytes,false);
            var loading=stoneCutouts.ReadBrowserPitTiles(stream);
            while(loading.MoveNext()) yield return loading.Current;
            Physics.SyncTransforms();
            Debug.Log($"BROWSER_PIT_TILES_LOADED {stoneCutouts.LastRebuiltSurfaceCount}");
        }
        public void DrawGrassSurface(UnityEngine.Rendering.CommandBuffer commands,Material material) =>
            stoneCutouts?.DrawSurfaces(commands,material);
        public const float FallLimit = -900f;
        public bool IsMapCollider(Collider collider) => collider is MeshCollider mesh && surfaces.Contains(mesh);

        public static bool IsOnMainIsland(float worldX,float worldZ,float minimumClearance=0f)
        {
            Vector2 mapPoint=new(worldX+WorldConfig.MapWidth*0.5f,
                worldZ+WorldConfig.MapLength*0.5f);
            return Contours[0].SignedDistance(mapPoint)>=minimumClearance;
        }

        // Green annotation in dsfsdfs.png, in the same photograph coordinates as
        // the island contours. The unpainted central hill is a hole in the mask.
        private static readonly Contour GrassArea = new(0f, -1,
            "507,614 518,580 541,560 573,558 602,577 619,609 624,646 633,674 635,711 678,738 731,766 783,786 838,796 883,770 939,741 966,719 994,714 1019,723 1045,741 1084,731 1116,724 1147,728 1173,746 1185,777 1190,812 1202,844 1203,884 1186,910 1158,924 1138,920 1109,910 1082,907 1037,923 991,936 940,940 913,961 880,979 843,990 843,1028 871,1060 901,1095 934,1141 956,1194 970,1230 967,1257 948,1278 920,1288 891,1281 870,1260 856,1226 839,1195 810,1161 780,1143 750,1129 715,1145 688,1171 657,1203 642,1242 643,1290 643,1329 628,1353 604,1366 564,1377 524,1393 480,1406 448,1403 421,1388 408,1363 405,1340 423,1307 450,1283 473,1242 481,1207 476,1175 492,1138 497,1110 466,1100 447,1078 441,1044 396,1043 359,1039 338,1030 329,1040 329,1070 315,1104 315,1155 326,1206 344,1237 368,1249 385,1265 394,1286 392,1314 375,1331 350,1345 318,1350 289,1372 282,1397 258,1421 226,1433 199,1428 177,1411 163,1388 164,1366 178,1346 201,1319 214,1301 214,1248 209,1205 210,1164 196,1131 204,1093 211,1052 211,1010 221,978 233,944 251,921 283,899 289,848 294,808 310,773 340,739 368,716 412,705 462,694 501,681 501,650");
        private static readonly Contour GrassHole = new(0f, -1,
            "473,827 511,815 542,797 555,827 568,869 539,896 507,913 469,929 450,921 462,888");

        public static bool IsGrassArea(float worldX, float worldZ)
            => GrassDensity(worldX, worldZ) > 0f;

        public static bool IsRiverbed(float worldX,float worldZ,float margin=0f)
        {
            Vector2 point=new(worldX+WorldConfig.MapWidth*0.5f,
                worldZ+WorldConfig.MapLength*0.5f);
            foreach(RiverbedPoint[] river in Riverbeds)
            for(int i=0;i+1<river.Length;i++)
            {
                RiverbedPoint a=river[i],b=river[i+1];
                Vector2 edge=b.Position-a.Position;
                float t=Mathf.Clamp01(Vector2.Dot(point-a.Position,edge)/
                    Mathf.Max(0.0001f,edge.sqrMagnitude));
                float halfWidth=Mathf.Lerp(a.Width,b.Width,t)*0.5f;
                if(Vector2.Distance(point,a.Position+edge*t)<=halfWidth+margin)
                    return true;
            }
            return false;
        }

        public static float GrassDensity(float worldX, float worldZ)
        {
            if(IsRiverbed(worldX,worldZ)) return 0f;
            Vector2 point = new(worldX + WorldConfig.MapWidth * 0.5f,
                worldZ + WorldConfig.MapLength * 0.5f);
            // Distance to either the outside boundary or the central unpainted
            // hole controls a broad, smooth density fade at every green edge.
            float clearance = Mathf.Min(GrassArea.SignedDistance(point),
                -GrassHole.SignedDistance(point));
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 150f, clearance));
        }

        public bool TryGetSurface(float x, float z, out RaycastHit hit)
        {
            Ray ray = new(new Vector3(x, 600f, z), Vector3.down);
            hit = default;
            return stoneCutouts!=null&&stoneCutouts.Raycast(ray,1800f,out hit);
        }

        public void Build()
        {
            var routine=BuildIncrementally();
            while(routine.MoveNext()) { }
        }

        internal System.Collections.IEnumerator BuildIncrementally()
        {
            if (WorldBootstrap.TestGround)
            {
                BuildTestGround();
                yield break;
            }
            Debug.Log("WYVERNLAND_STARTUP ground layout begin");
            GroundFeatureLayout.Initialize();
            Debug.Log("WYVERNLAND_STARTUP ground layout complete");
            yield return null;
            Shader shader = Resources.Load<Shader>("Clay");
            if (shader == null) throw new System.InvalidOperationException("Missing clay shader.");
            clay = new Material(shader) { name = "Unfired red-brown clay" };
            clay.SetColor("_BaseColor", WorldConfig.GroundColor);
            clay.SetFloat("_TerrainColorRegions", 1f);
            clay.SetColor("_DarkWoodColor",EnvironmentRegions.DarkBrown);
            var shaderRiverPoints=new System.Collections.Generic.List<Vector4>();
            foreach(RiverbedPoint[] river in Riverbeds)
            for(int i=0;i<river.Length;i++)
            {
                RiverbedPoint point=river[i];
                shaderRiverPoints.Add(new Vector4(
                    point.Position.x-WorldConfig.MapWidth*0.5f,
                    point.Position.y-WorldConfig.MapLength*0.5f,
                    point.Width,i+1<river.Length?1f:0f));
            }
            if(shaderRiverPoints.Count>80)
                throw new System.InvalidOperationException(
                    $"Riverbed shader supports 80 control points, received {shaderRiverPoints.Count}.");
            GetComponent<CachedWorldShading>()?.BakeTerrainRegions(clay,shaderRiverPoints.ToArray());
            for (int i = 0; i < 3; i++)
            {
                Debug.Log($"WYVERNLAND_STARTUP island {i+1} begin");
                Mesh cached=Resources.Load<Mesh>($"Terrain/Island{i+1}");
                if(cached!=null)
                {
                    var tiles = AddIslandIncrementally(i, cached);
                    while (tiles.MoveNext()) yield return tiles.Current;
                }
                else BuildIsland(i);
                Debug.Log($"WYVERNLAND_STARTUP island {i+1} complete");
                yield return null;
            }
            Physics.SyncTransforms();
        }

        private void BuildTestGround()
        {
            clay = new Material(Resources.Load<Shader>("Clay")) { name = "Test Ground red-brown clay" };
            clay.SetColor("_BaseColor", WorldConfig.GroundColor);
            clay.SetFloat("_TerrainColorRegions", 0f);
            Mesh mesh = new() { name = "200m x 200m plane" };
            mesh.vertices = new[] { new Vector3(-100,0,-100), new Vector3(-100,0,100),
                new Vector3(100,0,100), new Vector3(100,0,-100) };
            mesh.triangles = new[] { 0,1,2,0,2,3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            stoneCutouts??=new StoneTerrainCutout(transform);
            var tiles = stoneCutouts.AddTiledSurfaceIncrementally(mesh,clay,"Test Ground",collider=>surfaces.Add(collider));
            while (tiles.MoveNext()) { }
            Physics.SyncTransforms();
        }

        public Rect Bounds => WorldBootstrap.TestGround ? new Rect(-100,-100,200,200) : WorldConfig.MapBounds;

        public float HeightAt(float x, float z)
        {
            Ray ray = new(new Vector3(x, 500f, z), Vector3.down);
            return stoneCutouts!=null&&stoneCutouts.Raycast(ray,1600f,out var hit)
                ?hit.point.y:float.NegativeInfinity;
        }

        internal static float BaseHeightAt(float worldX,float worldZ)
        {
            Vector2 mapPoint=new(worldX+WorldConfig.MapWidth*0.5f,
                worldZ+WorldConfig.MapLength*0.5f);
            return EvaluateElevation(mapPoint,new float[Contours.Length],false);
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);

        // Ear clipping preserves every traced boundary vertex, including concave bays.
        private static System.Collections.Generic.List<Vector2[]> Triangulate(Vector2[] polygon)
        {
            var points = new System.Collections.Generic.List<Vector2>(polygon);
            float area = 0f;
            for (int i=0; i<points.Count; i++)
            {
                Vector2 a=points[i], b=points[(i+1)%points.Count];
                area += a.x*b.y-b.x*a.y;
            }
            if (area<0f) points.Reverse();
            var result = new System.Collections.Generic.List<Vector2[]>();
            while(points.Count>3)
            {
                bool found=false;
                for(int i=0;i<points.Count;i++)
                {
                    Vector2 a=points[(i+points.Count-1)%points.Count], b=points[i], c=points[(i+1)%points.Count];
                    if(Cross(a,b,c)<=0.001f) continue;
                    bool occupied=false;
                    for(int j=0;j<points.Count;j++)
                    {
                        Vector2 p=points[j];
                        if(p==a || p==b || p==c) continue;
                        if(Cross(a,b,p)>=0f && Cross(b,c,p)>=0f && Cross(c,a,p)>=0f) { occupied=true; break; }
                    }
                    if(occupied) continue;
                    result.Add(new[]{a,b,c}); points.RemoveAt(i); found=true; break;
                }
                if(!found) throw new System.InvalidOperationException("Island outline could not be triangulated.");
            }
            result.Add(points.ToArray());
            return result;
        }

        private static System.Collections.Generic.List<Vector2> Clip(
            System.Collections.Generic.List<Vector2> input, int axis, float boundary, bool greater)
        {
            var output=new System.Collections.Generic.List<Vector2>();
            if(input.Count==0) return output;
            Vector2 a=input[input.Count-1];
            float da=(greater?1f:-1f)*(a[axis]-boundary);
            foreach(Vector2 b in input)
            {
                float db=(greater?1f:-1f)*(b[axis]-boundary);
                if((da>=0f)!=(db>=0f)) output.Add(Vector2.Lerp(a,b,da/(da-db)));
                if(db>=0f) output.Add(b);
                a=b; da=db;
            }
            return output;
        }

        private static void CleanClippedPolygon(System.Collections.Generic.List<Vector2> polygon)
        {
            // Repeated clipping can emit the same boundary intersection twice.
            // Those zero-length edges made fan triangulation skip parts of a
            // refined cell and occasionally exposed huge underside triangles.
            for(int i=polygon.Count-1;i>=0&&polygon.Count>1;i--)
            {
                int previous=(i+polygon.Count-1)%polygon.Count;
                if((polygon[i]-polygon[previous]).sqrMagnitude<0.000001f)
                    polygon.RemoveAt(i);
            }
            bool removed=true;
            while(removed&&polygon.Count>3)
            {
                removed=false;
                for(int i=0;i<polygon.Count;i++)
                {
                    Vector2 a=polygon[(i+polygon.Count-1)%polygon.Count];
                    Vector2 b=polygon[i];
                    Vector2 c=polygon[(i+1)%polygon.Count];
                    if(Mathf.Abs(Cross(a,b,c))<0.0001f)
                    {
                        polygon.RemoveAt(i);
                        removed=true;
                        break;
                    }
                }
            }
        }

        private Mesh BuildIsland(int island)
        {
            // Keep the lowlands visibly faceted, but use a finer base lattice than
            // before. Elevated cells are split around an evaluated centre point
            // below, concentrating extra faces where hills change height fastest.
            // Ten-metre sampling preserves the narrowest marked riverbeds in
            // both the rendered mesh and its matching collision surface.
            const float cell=10f;
            const float elevatedDetailHeight=65f;
            var vertices=new System.Collections.Generic.List<Vector3>();
            var indices=new System.Collections.Generic.List<int>();
            var heights=new System.Collections.Generic.Dictionary<Vector2, float>();
            float[] distances=new float[Contours.Length];
            Vector3 Top(Vector2 p)
            {
                // Quantization shares positions at cell intersections.
                p=new Vector2(Mathf.Round(p.x*100f)/100f,Mathf.Round(p.y*100f)/100f);
                if(!heights.TryGetValue(p,out float h))
                {
                    h=EvaluateElevation(p,distances);
                    heights[p]=h;
                }
                return new Vector3(p.x-WorldConfig.MapWidth/2f,h,p.y-WorldConfig.MapLength/2f);
            }
            Vector3 Bottom(Vector2 p)
            {
                float inward=Mathf.Max(0f,Contours[island].SignedDistance(p));
                float depth=65f+Mathf.Min(540f,inward*0.65f);
                depth+=Mathf.PerlinNoise(p.x/220f,p.y/220f)*35f;
                return new Vector3(p.x-WorldConfig.MapWidth/2f,-depth,p.y-WorldConfig.MapLength/2f);
            }
            void Face(Vector3 a,Vector3 b,Vector3 c)
            {
                if(Vector3.Cross(b-a,c-a).sqrMagnitude<0.00001f) return;
                int index=vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c);
                indices.Add(index); indices.Add(index+1); indices.Add(index+2);
            }
            foreach(Vector2[] triangle in Triangulate(Contours[island].Points))
            {
                float minX=Mathf.Min(triangle[0].x,triangle[1].x,triangle[2].x);
                float maxX=Mathf.Max(triangle[0].x,triangle[1].x,triangle[2].x);
                float minZ=Mathf.Min(triangle[0].y,triangle[1].y,triangle[2].y);
                float maxZ=Mathf.Max(triangle[0].y,triangle[1].y,triangle[2].y);
                for(int x=Mathf.FloorToInt(minX/cell);x<=Mathf.FloorToInt(maxX/cell);x++)
                for(int z=Mathf.FloorToInt(minZ/cell);z<=Mathf.FloorToInt(maxZ/cell);z++)
                {
                    var xCuts=new System.Collections.Generic.List<float>{x*cell,(x+1)*cell};
                    var zCuts=new System.Collections.Generic.List<float>{z*cell,(z+1)*cell};
                    // Do not inject feature-local cuts into individual cells.
                    // Their border vertices do not propagate across every
                    // neighbouring contour triangle and can leave open strips.
                    // Embedded-rock depressions still affect shared lattice and
                    // elevated-detail vertices through EvaluateElevation.
                    for(int cutX=0;cutX+1<xCuts.Count;cutX++)
                    for(int cutZ=0;cutZ+1<zCuts.Count;cutZ++)
                    {
                    var clipped=new System.Collections.Generic.List<Vector2>(triangle);
                    clipped=Clip(clipped,0,xCuts[cutX],true);
                    clipped=Clip(clipped,0,xCuts[cutX+1],false);
                    clipped=Clip(clipped,1,zCuts[cutZ],true);
                    clipped=Clip(clipped,1,zCuts[cutZ+1],false);
                    CleanClippedPolygon(clipped);
                    if(clipped.Count<3) continue;
                    Vector2 centre=Vector2.zero;
                    foreach(Vector2 point in clipped) centre+=point;
                    centre/=clipped.Count;
                    bool addElevatedDetail=Top(centre).y>=elevatedDetailHeight;
                    if(addElevatedDetail)
                    {
                        // A centre fan preserves the cell boundary, so detailed
                        // mountain cells meet neighbouring lowland cells cleanly.
                        // Limit the centre against its boundary average so a
                        // contour crossing cannot form a single needle-like face.
                        Vector3 centreTop=Top(centre);
                        float boundaryHeight=0f;
                        foreach(Vector2 point in clipped) boundaryHeight+=Top(point).y;
                        boundaryHeight/=clipped.Count;
                        if(EnvironmentRegions.PitDepth(centre.x-WorldConfig.MapWidth*0.5f,
                            centre.y-WorldConfig.MapLength*0.5f)<=0f)
                            centreTop.y=Mathf.Clamp(centreTop.y,boundaryHeight-cell*0.45f,
                                boundaryHeight+cell*0.45f);
                        for(int k=0;k<clipped.Count;k++)
                        {
                            Vector2 a=clipped[k], b=clipped[(k+1)%clipped.Count];
                            Face(Top(a),centreTop,Top(b));
                        }
                    }
                    else
                    {
                        for(int k=1;k+1<clipped.Count;k++)
                        {
                            Vector2 a=clipped[0], b=clipped[k], c=clipped[k+1];
                            // Counterclockwise in X/Z points down in Unity.
                            Face(Top(a),Top(c),Top(b));
                        }
                    }
                    for(int k=1;k+1<clipped.Count;k++)
                    {
                        Vector2 a=clipped[0], b=clipped[k], c=clipped[k+1];
                        Face(Bottom(a),Bottom(b),Bottom(c));
                    }
                    }
                }
            }
            // Vertical cliff faces join matching top and underside boundary samples.
            Vector2[] coast=Contours[island].Points;
            float signedArea=0f;
            for(int i=0;i<coast.Length;i++) signedArea+=Cross(Vector2.zero,coast[i],coast[(i+1)%coast.Length]);
            for(int i=0;i<coast.Length;i++)
            {
                Vector2 a=coast[i], b=coast[(i+1)%coast.Length];
                var cuts=new System.Collections.Generic.List<float>{0f,1f};
                for(int axis=0;axis<2;axis++)
                {
                    float delta=b[axis]-a[axis];
                    if(Mathf.Abs(delta)<0.0001f) continue;
                    int first=Mathf.FloorToInt(Mathf.Min(a[axis],b[axis])/cell)+1;
                    int last=Mathf.FloorToInt(Mathf.Max(a[axis],b[axis])/cell);
                    for(int n=first;n<=last;n++)
                    {
                        float t=(n*cell-a[axis])/delta;
                        if(t>0f && t<1f) cuts.Add(t);
                    }
                }
                cuts.Sort();
                for(int j=0;j+1<cuts.Count;j++)
                {
                    Vector2 p=Vector2.Lerp(a,b,cuts[j]), q=Vector2.Lerp(a,b,cuts[j+1]);
                    if(signedArea<0f) { Vector2 swap=p;p=q;q=swap; }
                    Face(Top(p),Top(q),Bottom(q));
                    Face(Top(p),Bottom(q),Bottom(p));
                }
            }
            Mesh mesh=new() { name=$"Floating island {island+1} — flat facets", indexFormat=IndexFormat.UInt32 };
            mesh.SetVertices(vertices); mesh.SetTriangles(indices,0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
            ownedMeshes.Add(mesh);
            AddIsland(island,mesh);
            return mesh;
        }

        private void AddIsland(int island,Mesh mesh)
        {
            var tiles = AddIslandIncrementally(island, mesh);
            while (tiles.MoveNext()) { }
        }

        private System.Collections.IEnumerator AddIslandIncrementally(int island, Mesh mesh)
        {
            stoneCutouts??=new StoneTerrainCutout(transform);
            var tiles = stoneCutouts.AddTiledSurfaceIncrementally(mesh,clay,$"Floating Island {island+1}",collider=>surfaces.Add(collider));
            while (tiles.MoveNext()) yield return tiles.Current;
        }

        private void OnDestroy()
        {
            stoneCutouts?.Dispose();
            foreach(Mesh mesh in ownedMeshes)
                if(Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            if(clay!=null)
            {
                if(Application.isPlaying) Destroy(clay); else DestroyImmediate(clay);
            }
        }

        private static float EvaluateElevation(Vector2 point,float[] distances,
            bool applyGroundFeatures=true,bool applyRiverbeds=true)
        {
            int enclosing = -1;
            float coastDistance = float.NegativeInfinity;
            for (int i = 0; i < Contours.Length; i++)
            {
                if (Contours[i].Parent >= 0) continue;
                distances[i] = Contours[i].SignedDistance(point);
                coastDistance = Mathf.Max(coastDistance, distances[i]);
                if (distances[i] >= 0f) enclosing = i;
            }
            if (enclosing < 0)
                return Mathf.Max(-WorldConfig.SeaFloorDepth, coastDistance * 0.3f);
            int island = enclosing;

            // Follow this island's hierarchy, then interpolate between drawn loops.
            for (int i = enclosing + 1; i < Contours.Length; i++)
            {
                if (Contours[i].Parent != enclosing) continue;
                distances[i] = Contours[i].SignedDistance(point);
                if (distances[i] >= 0f) enclosing = i;
            }

            float lowerDistance = Mathf.Max(0f, distances[enclosing]);
            float upperDistance = float.PositiveInfinity;
            for (int i = 0; i < Contours.Length; i++)
                if (Contours[i].Parent == enclosing)
                    upperDistance = Mathf.Min(upperDistance, Mathf.Abs(distances[i]));

            float elevation = Contours[enclosing].Elevation;
            if (!float.IsPositiveInfinity(upperDistance))
            {
                float contourBlend=lowerDistance/Mathf.Max(0.001f,lowerDistance+upperDistance);
                // Zero slope at both drawn contours prevents visible creases where
                // adjacent 100 m elevation bands meet.
                elevation+=100f*Mathf.SmoothStep(0f,1f,contourBlend);
            }
            else
            {
                // A bounded smoothstep rounds terminal contours into summits. The
                // previous sub-linear power curve had an infinite derivative at
                // the contour and amplified medial-axis ridges into sharp spines.
                float summitBlend=Mathf.InverseLerp(0f,55f,lowerDistance);
                elevation+=40f*Mathf.SmoothStep(0f,1f,summitBlend);
            }

            // Keep the subtle Perlin roughness off the exact contour boundaries.
            float boundaryDistance = Mathf.Min(lowerDistance, upperDistance);
            float noise = (Mathf.PerlinNoise(point.x / 100f + 31.7f, point.y / 100f + 74.1f) - 0.5f) * 2f;
            elevation += noise * Mathf.Clamp01(boundaryDistance / 20f);
            if (island == 0)
                elevation = ApplyPlains(point, elevation);
            if(applyRiverbeds)
                elevation-=RiverbedDepression(point);
            if (applyGroundFeatures)
                elevation-=GroundFeatureLayout.DepressionAt(point);
            // Do not clamp the upper value: the highest drawn contour is a
            // shoulder of the mountain, not a horizontal cutting plane.
            return Mathf.Max(0f, elevation)-EnvironmentRegions.PitDepth(
                point.x-WorldConfig.MapWidth*0.5f,point.y-WorldConfig.MapLength*0.5f);
        }

        private static RiverbedPoint[][] BuildAdjustedRiverbeds()
        {
            const float sampleSpacing=140f;
            const float searchRadius=110f;
            const float searchStep=10f;
            var adjusted=new RiverbedPoint[AuthoredRiverbeds.Length][];
            float[] distances=new float[Contours.Length];
            for(int riverIndex=0;riverIndex<AuthoredRiverbeds.Length;riverIndex++)
            {
                RiverbedPoint[] source=AuthoredRiverbeds[riverIndex];
                var points=new System.Collections.Generic.List<RiverbedPoint>();
                for(int segment=0;segment+1<source.Length;segment++)
                {
                    RiverbedPoint a=source[segment],b=source[segment+1];
                    Vector2 edge=b.Position-a.Position;
                    int steps=Mathf.Max(1,Mathf.CeilToInt(edge.magnitude/sampleSpacing));
                    Vector2 across=new(-edge.y,edge.x);
                    across=across.sqrMagnitude>0.0001f?across.normalized:Vector2.right;
                    for(int step=0;step<steps;step++)
                    {
                        float t=step/(float)steps;
                        RiverbedPoint authored=new(0f,0f,
                            Mathf.Lerp(a.Width,b.Width,t),Mathf.Lerp(a.Depth,b.Depth,t));
                        Vector2 centre=Vector2.Lerp(a.Position,b.Position,t);
                        Vector2 lowest=centre;
                        float lowestHeight=float.PositiveInfinity;
                        for(float offset=-searchRadius;offset<=searchRadius;offset+=searchStep)
                        {
                            Vector2 candidate=centre+across*offset;
                            if(Contours[0].SignedDistance(candidate)<0f) continue;
                            float height=EvaluateElevation(candidate,distances,false,false);
                            if(height<lowestHeight)
                            {
                                lowestHeight=height;
                                lowest=candidate;
                            }
                        }
                        points.Add(new RiverbedPoint(
                            40f+lowest.x/WorldConfig.MapWidth*1210f,
                            1660f-lowest.y/WorldConfig.MapLength*1510f,
                            authored.Width,authored.Depth));
                    }
                }
                RiverbedPoint last=source[source.Length-1];
                Vector2 finalEdge=last.Position-source[source.Length-2].Position;
                Vector2 finalAcross=new(-finalEdge.y,finalEdge.x);
                finalAcross=finalAcross.sqrMagnitude>0.0001f?
                    finalAcross.normalized:Vector2.right;
                Vector2 finalLowest=last.Position;
                float finalHeight=float.PositiveInfinity;
                for(float offset=-searchRadius;offset<=searchRadius;offset+=searchStep)
                {
                    Vector2 candidate=last.Position+finalAcross*offset;
                    if(Contours[0].SignedDistance(candidate)<0f) continue;
                    float height=EvaluateElevation(candidate,distances,false,false);
                    if(height<finalHeight)
                    {
                        finalHeight=height;
                        finalLowest=candidate;
                    }
                }
                points.Add(new RiverbedPoint(
                    40f+finalLowest.x/WorldConfig.MapWidth*1210f,
                    1660f-finalLowest.y/WorldConfig.MapLength*1510f,
                    last.Width,last.Depth));
                adjusted[riverIndex]=points.ToArray();
            }
            return adjusted;
        }

        private static float RiverbedDepression(Vector2 point)
        {
            float depression=0f;
            foreach(RiverbedPoint[] river in Riverbeds)
            for(int i=0;i+1<river.Length;i++)
            {
                RiverbedPoint a=river[i],b=river[i+1];
                Vector2 edge=b.Position-a.Position;
                float t=Mathf.Clamp01(Vector2.Dot(point-a.Position,edge)/
                    Mathf.Max(0.0001f,edge.sqrMagnitude));
                float distance=Vector2.Distance(point,a.Position+edge*t);
                float halfWidth=Mathf.Lerp(a.Width,b.Width,t)*0.5f;
                // Preserve the requested bank-to-bank width as the main bed,
                // with a shallow five-metre shoulder that smoothly rejoins the
                // terrain and guarantees sampling of the narrowest sections.
                float influence=1f-Mathf.SmoothStep(0f,1f,
                    Mathf.InverseLerp(halfWidth*0.55f,halfWidth+5.25f,distance));
                depression=Mathf.Max(depression,
                    Mathf.Lerp(a.Depth,b.Depth,t)*influence);
            }
            return depression;
        }

        private static float ApplyPlains(Vector2 point, float elevation)
        {
            float influence = 0f;
            foreach (Contour plain in PlainAreas)
            {
                // The annotated edge fades over roughly 160 m. A little influence
                // outside the brush stroke prevents a visible ridge at its boundary.
                float distance = plain.SignedDistance(point);
                influence = Mathf.Max(influence, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(-110f, 55f, distance)));
            }
            if (influence <= 0f) return elevation;

            // Broad 400–900 m swells form readable plains; the smaller layer keeps
            // them from feeling level or machine-smoothed.
            float broad = Mathf.PerlinNoise(point.x / 720f + 18.2f, point.y / 720f + 37.6f);
            float rolling = Mathf.PerlinNoise(point.x / 310f + 61.8f, point.y / 310f + 12.4f);
            float detail = Mathf.PerlinNoise(point.x / 125f + 9.3f, point.y / 125f + 88.1f);
            float plainHeight = 22f + (broad - 0.5f) * 20f +
                (rolling - 0.5f) * 10f + (detail - 0.5f) * 3f;
            return Mathf.Lerp(elevation, Mathf.Max(7f, plainHeight), influence * 0.94f);
        }

    }
}
