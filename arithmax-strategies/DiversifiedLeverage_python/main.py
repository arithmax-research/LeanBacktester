# region imports
from AlgorithmImports import *
from datetime import datetime, timedelta
# endregion

class DiversifiedLeverage(QCAlgorithm):

    def Initialize(self):
        # Locally Lean installs free sample data, to download more data please visit https://www.quantconnect.com/docs/v2/lean-cli/datasets/downloading-data
        self.SetStartDate(2017, 1, 1)  # Set Start Date
        self.SetEndDate(2025, 6, 30)   # Set End Date
        self.SetCash(100000)           # Set Strategy Cash

        # Initialize portfolio weights and symbols
        self.target_weights = {
            "TQQQ": 0.20,  # 3x Leveraged Nasdaq
            "UPRO": 0.20,  # 3x Leveraged S&P 500
            "UDOW": 0.10,  # 3x Leveraged Dow Jones
            "TMF": 0.25,   # 3x Leveraged Treasury Bonds
            "UGL": 0.10,   # 3x Leveraged Gold
            "DIG": 0.15,   # 2x Leveraged Oil and Gas Companies
        }

        self.symbols = list(self.target_weights.keys())
        self.order_dict = {}
        self.rebalance_period = 4  # Rebalance every 4 days
        self.last_rebalance_time = datetime.min

        # Add securities to the algorithm
        for symbol in self.symbols:
            self.AddEquity(symbol, Resolution.Daily)

        # Schedule rebalancing
        self.Schedule.On(
            self.DateRules.Every(DayOfWeek.Monday),
            self.TimeRules.At(9, 31),
            self.RebalancePortfolio
        )
        
        # Log initial portfolio value
        self.Log(f"Initial Portfolio Value: ${self.Portfolio.TotalPortfolioValue:.2f}")
        weights_str = ", ".join([f"{k}: {v:.2%}" for k, v in self.target_weights.items()])
        self.Log(f"Target Portfolio Weights: {weights_str}")

    def OnData(self, data: Slice):
        '''OnData event is the primary entry point for your algorithm. Each new data point will be pumped in here.
            Arguments:
                data: Slice object keyed by symbol containing the stock data
        '''
        # Check if it's time to rebalance (every 4 days)
        if (self.Time - self.last_rebalance_time).days >= self.rebalance_period:
            self.RebalancePortfolio()

    def RebalancePortfolio(self):
        # Skip if we have pending orders
        pending_orders = [order for order in self.order_dict.values() 
                         if order.Status in [OrderStatus.Submitted, OrderStatus.PartiallyFilled]]
        if pending_orders:
            self.Log("Skipping rebalancing - pending orders exist")
            return

        self.Log("Rebalancing portfolio...")
        
        # Get current portfolio value
        portfolio_value = self.Portfolio.TotalPortfolioValue
        self.Log(f"Current Portfolio Value: ${portfolio_value:.2f}")

        # Clear completed orders
        self.order_dict.clear()

        # Calculate desired position values and create orders
        for symbol, weight in self.target_weights.items():
            if symbol not in self.Securities:
                self.Log(f"Warning: {symbol} not in securities, skipping")
                continue

            security = self.Securities[symbol]
            if not security.HasData:
                self.Log(f"Warning: {symbol} has no data, skipping")
                continue

            current_price = security.Price
            if current_price <= 0:
                self.Log(f"Warning: {symbol} has invalid price {current_price}, skipping")
                continue

            # Calculate target position value and shares
            target_value = portfolio_value * weight
            target_shares = int(target_value / current_price)

            # Get current position
            current_shares = self.Portfolio[symbol].Quantity

            # Calculate difference
            shares_difference = target_shares - current_shares

            # Skip if the difference is very small
            if abs(shares_difference) < 1:
                continue

            # Create the order
            order = None
            if shares_difference > 0:
                self.Log(f"Buying {shares_difference} shares of {symbol} at ${current_price:.2f}")
                order = self.MarketOrder(symbol, shares_difference)
            elif shares_difference < 0:
                self.Log(f"Selling {abs(shares_difference)} shares of {symbol} at ${current_price:.2f}")
                order = self.MarketOrder(symbol, shares_difference)

            if order is not None:
                self.order_dict[symbol] = order

        self.last_rebalance_time = self.Time
        self.Log(f"Next portfolio rebalancing will be in {self.rebalance_period} day(s)")

    def OnOrderEvent(self, orderEvent: OrderEvent):
        order = self.Transactions.GetOrderById(orderEvent.OrderId)
        
        if orderEvent.Status == OrderStatus.Filled:
            if order.Direction == OrderDirection.Buy:
                self.Log(f"BUY EXECUTED - {orderEvent.Symbol}, Price: ${orderEvent.FillPrice:.2f}, Quantity: {orderEvent.FillQuantity}")
            else:
                self.Log(f"SELL EXECUTED - {orderEvent.Symbol}, Price: ${orderEvent.FillPrice:.2f}, Quantity: {orderEvent.FillQuantity}")
        elif orderEvent.Status in [OrderStatus.Canceled, OrderStatus.Invalid]:
            self.Log(f"Order {orderEvent.Symbol} Failed with Status: {orderEvent.Status}")

    def OnEndOfAlgorithm(self):
        self.Log(f"Final Portfolio Value: ${self.Portfolio.TotalPortfolioValue:.2f}")
        
        # Log final holdings
        for symbol, holding in self.Portfolio.items():
            if holding.Invested:
                weight_pct = holding.HoldingsValue / self.Portfolio.TotalPortfolioValue
                self.Log(f"{symbol}: {holding.Quantity} shares, Value: ${holding.HoldingsValue:.2f}, Weight: {weight_pct:.2%}")
