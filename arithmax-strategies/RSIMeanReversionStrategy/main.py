from AlgorithmImports import *
from datetime import datetime, timedelta

class RsiMeanReversion(QCAlgorithm):
    def Initialize(self):
        self.SetStartDate(2020, 1, 1)
        self.SetEndDate(2023, 12, 31)
        self.SetCash(100000)
        
        self.symbol = self.AddEquity("SPY", Resolution.Daily).Symbol
        self.rsi_period = 14
        self.rsi = RelativeStrengthIndex(self.symbol, self.rsi_period, MovingAverageType.Simple, Resolution.Daily, Field.Close)
        self.RegisterIndicator(self.symbol, self.rsi, Resolution.Daily)
        
        self.entry_rsi_threshold = 30
        self.exit_rsi_threshold = 70
        self.portfolio_percentage = 0.95
        self.is_long = False
        self.entry_price = 0
    
    def OnData(self, data: Slice):
        if not data.ContainsKey(self.symbol):
            return
        
        if not self.rsi.IsReady:
            return
        
        price = data[self.symbol].Close
        
        if not self.is_long and self.rsi.Current.Value < self.entry_rsi_threshold:
            if self.Portfolio.Cash > 0:
                self.entry_price = price
                quantity = self.CalculateOrderQuantity(self.symbol, self.portfolio_percentage)
                self.Buy(self.symbol, quantity)
                self.is_long = True
                self.Log(f"Buy {self.symbol} at {price} - RSI: {self.rsi.Current.Value}")
                self.Log(f"Portfolio Value: {self.Portfolio.TotalPortfolioValue}")
        
        elif self.is_long and self.rsi.Current.Value > self.exit_rsi_threshold:
            self.Sell(self.symbol, self.Portfolio[self.symbol].Quantity)
            self.is_long = False
            self.Log(f"Sell {self.symbol} at {price} - RSI: {self.rsi.Current.Value}")
            self.Log(f"Portfolio Value: {self.Portfolio.TotalPortfolioValue}")
    
    def OnOrderEvent(self, orderEvent: OrderEvent):
        if orderEvent.Status == OrderStatus.Filled:
            self.Log(f"Order filled: {orderEvent}")
