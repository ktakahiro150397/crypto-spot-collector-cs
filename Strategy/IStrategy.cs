public interface IStrategyDecisioner
{
    /// <summary>
    /// 指定された日時点でのOHLCVデータに基づき、売買シグナルを判断する
    /// </summary>
    /// <param name="symbol">取引ペアのシンボル（例："ETH"）</param>
    /// <param name="position">現在のポジション情報</param>
    /// <param name="ohlcvData">指定日時点までのOHLCVデータのリスト</param>
    /// <returns>売買シグナルの判断結果を含むStrategyDecisionResultオブジェクト。ポジション変更ない場合はnull</returns>
    StrategyDecisionResult DecideSignal(string symbol, PerpetualPosition position, List<OhlcvData> ohlcvData);
}