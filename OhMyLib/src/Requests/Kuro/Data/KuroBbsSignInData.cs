namespace OhMyLib.Requests.Kuro.Data;

/**
{
  "continueDays": 2,
  "gainVoList": [
    {
      "gainTyp": 2,
      "gainValue": 30
    }
  ],
  "geeTest": false,
  "totalSignInDay": 5
}
 */
public class KuroBbsSignInData
{
    public int ContinueDays { get; set; }
    public List<GainVo> GainVoList { get; set; } = [];
    public bool GeeTest { get; set; }
    public int TotalSignInDay { get; set; }

    public class GainVo
    {
        public int GainTyp { get; set; }
        public int GainValue { get; set; }
    }
}