from AlgorithmImports import *
from datetime import datetime, timedelta

class EMACrossoverMomentum(QCAlgorithm):
    def Initialize(self):
        self.SetStartDate(2019, 1, 1)
        self.SetEndDate(2023, 12, 31)
        self.SetCash(100000)
        
        self.symbol = "QQQ"
        self.resolution = Resolution.Daily
        self.AddEquity(self.symbol, self.resolution)
        
        self.fast_ema_period = 20
        self.slow_ema_period = 50
        
        self.fast_ema = ExponentialMovingAverage(self.fast_ema_period)
        self.slow_ema = ExponentialMovingAverage(self.slow_ema_period)
        
        self.ema_cross_over_event = False
        self.entry_price = 0
        self.exit_price = 0
        
        self.SetWarmUp(self.slow_ema_period)
    
    def OnData(self, data: Slice):
        if not data.ContainsKey(self.symbol):
            return
        
        close = data[self.symbol].Close
        self.fast_ema.Update(close.Time, close)
        self.slow_ema.Update(close.Time, close)
        
        if self.fast_ema.IsReady and self.slow_ema.IsReady:
            fast_ema_value = self.fast_ema.Current.Value
            slow_ema_value = self.slow_ema.Current.Value
            
            if fast_ema_value > slow_ema_value and self.fast_ema.IsReady and self.slow_ema.IsReady and not self.Portfolio[self.symbol].Invested:
                self.Log(f"Bullish crossover: {close.Time} - Fast EMA: {fast_ema_value:.2f}, Slow EMA: {slow_ema_value:.2f}")
                self.entry_price = close
                self.SetHoldings(self.symbol, 0.9)
                self.Log(f"Bought {self.symbol} at {self.entry_price:.2f}")
                self.Log(f"Portfolio Value: {self.Portfolio.TotalPortfolioValue:.2f}")
            
            elif fast_ema_value < slow_ema_value and self.fast_ema.IsReady and self.slow_ema.IsReady and self.Portfolio[self.symbol].Invested:
                self.Log(f"Bearish crossover: {close.Time} - Fast EMA: {fast_ema_value:.2f}, Slow EMA: {slow_ema_value:.2f}")
                self.exit_price = close
                self.Liquidate(self.symbol)
                self.Log(f"Sold {self.symbol} at {self.exit_price:.2f}")
                self.Log(f"Portfolio Value: {self.Portfolio.TotalPortfolioValue:.2f}")
    
    def OnSecuritiesChanged(self, changes: SecurityChanges):
        for security in changes.AddedSecurities:
            symbol = security.Symbol
            if symbol == self.symbol:
                self.RegisterIndicator(symbol, self.fast_ema, Resolution.Daily)
                self.RegisterIndicator(symbol, self.slow_ema, Resolution.Daily)
