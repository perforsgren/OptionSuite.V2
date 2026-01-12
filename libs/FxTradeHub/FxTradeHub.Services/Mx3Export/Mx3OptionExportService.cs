using System;
using System.Globalization;
using System.Xml;
using FxTradeHub.Contracts.Dtos;
using FxSharedConfig;
using System.IO;

namespace FxTradeHub.Services.Mx3Export
{
    /// <summary>
    /// D4.2a: Service för att exportera option-trades till MX3 XML-format.
    /// Refaktorerad från legacy blotter kod - ren affärslogik utan UI-beroenden.
    /// </summary>
    public sealed class Mx3OptionExportService
    {

        /// <summary>
        /// D4.2a: Skapar en MX3 XML-fil för en option-trade.
        /// </summary>
        /// <param name="request">Trade-data som ska exporteras</param>
        /// <returns>Resultat med filnamn och path, eller error om något gick fel</returns>
        public Mx3OptionExportResult CreateXmlFile(Mx3OptionExportRequest request)
        {
            try
            {
                var exportFolder = AppPaths.Mx3ImportFolder;

                // NYTT FORMAT: {StpTradeId}_{TradeId}.xml
                var fileName = $"{request.StpTradeId}_{request.TradeId}.xml";
                var fullPath = Path.Combine(exportFolder, fileName);

                // Skapa XML
                var xmlDoc = BuildXmlDocument(request);

                // Spara
                xmlDoc.Save(fullPath);

                return new Mx3OptionExportResult
                {
                    Success = true,
                    FileName = fileName,
                    FilePath = fullPath
                };
            }
            catch (Exception ex)
            {
                return new Mx3OptionExportResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }


        /// <summary>
        /// D4.2a: Bygger MX3 XML-dokumentet för en option trade.
        /// Följer exakt strukturen från legacy blotter (beprövad mot MX3).
        /// </summary>
        private XmlDocument BuildXmlDocument(Mx3OptionExportRequest req)
        {
            var xmlDoc = new XmlDocument();

            // Parse currency pair
            var ccy1 = req.CurrencyPair.Substring(0, 3);
            var ccy2 = req.CurrencyPair.Substring(3, 3);

            // Format dates
            var tradedate = req.TradeDate.ToString("yyyyMMdd");
            var settlementdate = req.SettlementDate.ToString("yyyyMMdd");
            var expirydate = req.ExpiryDate.ToString("yyyyMMdd");
            var premiumdate = req.PremiumDate.ToString("yyyyMMdd");

            // Calculate notionals
            decimal notional1, notional2;
            if (ccy1 == req.NotionalCurrency)
            {
                notional1 = req.Notional;
                notional2 = req.Notional * req.Strike;
            }
            else
            {
                notional2 = req.Notional;
                notional1 = req.Notional / req.Strike;
            }

            // Adjust MIC if needed
            var mic = req.MIC == "SWBI" ? "XOFF" : req.MIC;

            #region Root: MxML
            var rootNode = xmlDoc.CreateElement("MxML");
            var attribute = xmlDoc.CreateAttribute("version");
            attribute.Value = "1-1";
            rootNode.Attributes.Append(attribute);
            xmlDoc.AppendChild(rootNode);
            #endregion

            #region events
            var eventsNode = xmlDoc.CreateElement("events");
            rootNode.AppendChild(eventsNode);

            var mainEventNode = xmlDoc.CreateElement("mainEvent");
            eventsNode.AppendChild(mainEventNode);

            AppendElement(xmlDoc, mainEventNode, "action", "insertion");

            var objectNode = xmlDoc.CreateElement("object");
            SetAttribute(xmlDoc, objectNode, "href", "#contract_0");
            mainEventNode.AppendChild(objectNode);

            AppendElement(xmlDoc, objectNode, "objectNature", "contract");

            var inputByNode = xmlDoc.CreateElement("inputBy");
            mainEventNode.AppendChild(inputByNode);

            AppendElement(xmlDoc, inputByNode, "userName", Environment.UserName.ToUpper());
            AppendElement(xmlDoc, inputByNode, "group", "FO");
            AppendElement(xmlDoc, inputByNode, "desk", "STKFXD");
            #endregion

            #region contracts
            var contractsNode = xmlDoc.CreateElement("contracts");
            rootNode.AppendChild(contractsNode);

            var contractNode = xmlDoc.CreateElement("contract");
            SetAttribute(xmlDoc, contractNode, "id", "contract_0");
            SetAttribute(xmlDoc, contractNode, "mefClass", "mxContractISINGLE");
            SetAttribute(xmlDoc, contractNode, "mefClassInstanceLabel", "Simple Option");
            contractsNode.AppendChild(contractNode);

            var contractHeaderNode = xmlDoc.CreateElement("contractHeader");
            contractNode.AppendChild(contractHeaderNode);

            var contractCategoryNode = xmlDoc.CreateElement("contractCategory");
            contractHeaderNode.AppendChild(contractCategoryNode);
            AppendElement(xmlDoc, contractCategoryNode, "typology", "FXD: Simple Option");

            var contractSourceNode = xmlDoc.CreateElement("contractSource");
            contractHeaderNode.AppendChild(contractSourceNode);
            AppendElement(xmlDoc, contractSourceNode, "sourceModule", "mx");

            var contractComponentsNode = xmlDoc.CreateElement("contractComponents");
            contractNode.AppendChild(contractComponentsNode);

            var contractComponentNode = xmlDoc.CreateElement("contractComponent");
            contractComponentsNode.AppendChild(contractComponentNode);
            AppendElement(xmlDoc, contractComponentNode, "typologyId", "0");

            var tradeReferenceNode = xmlDoc.CreateElement("tradeReference");
            SetAttribute(xmlDoc, tradeReferenceNode, "href", "#trade_0");
            contractComponentNode.AppendChild(tradeReferenceNode);
            #endregion

            #region Trades
            var tradesNode = xmlDoc.CreateElement("trades");
            rootNode.AppendChild(tradesNode);

            var tradeNode = xmlDoc.CreateElement("trade");
            SetAttribute(xmlDoc, tradeNode, "id", "trade_0");
            SetAttribute(xmlDoc, tradeNode, "mefClass", "mxContractITRADE");
            tradesNode.AppendChild(tradeNode);

            BuildTradeParties(xmlDoc, tradeNode, req.CounterpartId, req.Counterpart);
            BuildTradePortfolios(xmlDoc, tradeNode, req.Portfolio, req.Margin, req.ReportingEntity);
            BuildTradeHeader(xmlDoc, tradeNode, tradedate, req.Portfolio, req.Trader, req.CounterpartId,
                req.TradeId, req.ExecutionTime, req.Margin, req.Broker, req.ISIN, mic, req.TVTIC, req.InvID, req.ReportingEntity);
            BuildTradeBody(xmlDoc, tradeNode, req.BuySell, req.CallPut, req.Portfolio, req.CounterpartId,
                expirydate, req.Cut, settlementdate, ccy1, ccy2, notional1, notional2,
                req.Strike, premiumdate, req.PremiumCurrency, req.Premium);
            BuildTradeInputConditions(xmlDoc, tradeNode, req.Trader, tradedate);
            #endregion

            return xmlDoc;
        }

        #region Helper methods for XML building

        private void BuildTradeParties(XmlDocument xmlDoc, XmlNode tradeNode, string counterpartId, string counterpart)
        {
            var partiesNode = xmlDoc.CreateElement("parties");
            tradeNode.AppendChild(partiesNode);

            var party1 = xmlDoc.CreateElement("party");
            SetAttribute(xmlDoc, party1, "id", "SWEDBANK");
            partiesNode.AppendChild(party1);
            AppendElement(xmlDoc, party1, "partyName", "SWEDBANK");

            var party2 = xmlDoc.CreateElement("party");
            SetAttribute(xmlDoc, party2, "id", "_" + counterpartId);
            partiesNode.AppendChild(party2);
            AppendElement(xmlDoc, party2, "partyName", counterpart);
        }

        private void BuildTradePortfolios(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio, decimal? margin, string reportingEntity)
        {
            var portfoliosNode = xmlDoc.CreateElement("portfolios");
            tradeNode.AppendChild(portfoliosNode);

            // Main portfolio
            var portfolioNode = xmlDoc.CreateElement("portfolio");
            SetAttribute(xmlDoc, portfolioNode, "id", portfolio);
            portfoliosNode.AppendChild(portfolioNode);
            AppendElement(xmlDoc, portfolioNode, "portfolioLabel", portfolio);

            // Margin portfolio (if margin exists)
            if (margin.HasValue && margin.Value != 0)
            {
                var marginPortfolio = DetermineMarginPortfolio(reportingEntity);
                var portfolioNodeMargin = xmlDoc.CreateElement("portfolio");
                SetAttribute(xmlDoc, portfolioNodeMargin, "id", marginPortfolio);
                portfoliosNode.AppendChild(portfolioNodeMargin);
                AppendElement(xmlDoc, portfolioNodeMargin, "portfolioLabel", marginPortfolio);
            }
        }

        private void BuildTradeHeader(XmlDocument xmlDoc, XmlNode tradeNode, string tradedate, string portfolio,
            string trader, string counterpartId, string tradeId, string executionTime, decimal? margin,
            string broker, string isin, string mic, string tvtic, string invId, string reportingEntity)
        {
            var tradeHeaderNode = xmlDoc.CreateElement("tradeHeader");
            tradeNode.AppendChild(tradeHeaderNode);

            AppendElement(xmlDoc, tradeHeaderNode, "tradeDate", tradedate);

            // Trade category
            var tradeCategoryNode = xmlDoc.CreateElement("tradeCategory");
            tradeHeaderNode.AppendChild(tradeCategoryNode);
            AppendElement(xmlDoc, tradeCategoryNode, "tradeDestination", "external");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeFamily", "CURR");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeGroup", "OPT");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeType", "SMP");

            // Trade views
            BuildTradeViews(xmlDoc, tradeHeaderNode, portfolio, trader, counterpartId);

            // User defined fields
            BuildUserDefinedFields(xmlDoc, tradeHeaderNode, tradeId, executionTime, margin, broker, isin, mic, tvtic, invId, reportingEntity);

            // Trade fees (margin)
            if (margin.HasValue && margin.Value != 0)
            {
                BuildTradeFees(xmlDoc, tradeHeaderNode, portfolio, margin.Value, tradedate, reportingEntity);
            }
        }

        private void BuildTradeViews(XmlDocument xmlDoc, XmlNode tradeHeaderNode, string portfolio, string trader, string counterpartId)
        {
            var tradeViewsNode = xmlDoc.CreateElement("tradeViews");
            tradeHeaderNode.AppendChild(tradeViewsNode);

            // View 1: Swedbank
            var tradeView1 = xmlDoc.CreateElement("tradeView");
            tradeViewsNode.AppendChild(tradeView1);

            var partyRef1 = xmlDoc.CreateElement("partyReference");
            SetAttribute(xmlDoc, partyRef1, "href", "#SWEDBANK");
            tradeView1.AppendChild(partyRef1);

            var portfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, portfolioRef, "href", "#" + portfolio);
            partyRef1.AppendChild(portfolioRef);

            AppendElement(xmlDoc, tradeView1, "userName", trader);

            // View 2: Counterpart
            var tradeView2 = xmlDoc.CreateElement("tradeView");
            tradeViewsNode.AppendChild(tradeView2);

            var partyRef2 = xmlDoc.CreateElement("partyReference");
            SetAttribute(xmlDoc, partyRef2, "href", "#_" + counterpartId);
            tradeView2.AppendChild(partyRef2);
        }

        private void BuildUserDefinedFields(XmlDocument xmlDoc, XmlNode tradeHeaderNode, string tradeId,
            string executionTime, decimal? margin, string broker, string isin, string mic, string tvtic,
            string invId, string reportingEntity)
        {
            var udfNode = xmlDoc.CreateElement("tradeUserDefinedFields");
            tradeHeaderNode.AppendChild(udfNode);

            // FO_COMMENT
            AppendUserDefinedField(xmlDoc, udfNode, "FO_COMMENT", tradeId, "character");

            // EXEC_TIME
            AppendUserDefinedField(xmlDoc, udfNode, "EXEC_TIME", executionTime, "character");

            // SALES (margin)
            if (margin.HasValue && margin.Value != 0)
            {
                AppendUserDefinedField(xmlDoc, udfNode, "SALES", margin.Value.ToString(CultureInfo.InvariantCulture), "numeric");
            }

            // BROKER
            if (!string.IsNullOrEmpty(broker))
            {
                AppendUserDefinedField(xmlDoc, udfNode, "L_BROKER", broker, "character");
            }

            // ISIN
            if (!string.IsNullOrEmpty(isin))
            {
                AppendUserDefinedField(xmlDoc, udfNode, "MIFID_ISIN", isin, "character");
            }

            // MIC
            AppendUserDefinedField(xmlDoc, udfNode, "MIFID_MIC", mic, "character");

            // TVTIC
            if (!string.IsNullOrEmpty(tvtic))
            {
                AppendUserDefinedField(xmlDoc, udfNode, "TVTIC", tvtic, "character");
            }

            // INV_DEC_ID
            AppendUserDefinedField(xmlDoc, udfNode, "INV_DEC_ID", invId, "character");

            // REPORT_ENT
            var reportEnt = !string.IsNullOrEmpty(reportingEntity) ? reportingEntity : "FX VOLATILITY";
            AppendUserDefinedField(xmlDoc, udfNode, "REPORT_ENT", reportEnt, "character");
        }

        private void AppendUserDefinedField(XmlDocument xmlDoc, XmlNode parentNode, string label, string value, string type)
        {
            var udfNode = xmlDoc.CreateElement("userDefinedField");
            parentNode.AppendChild(udfNode);

            AppendElement(xmlDoc, udfNode, "fieldLabel", label);
            AppendElement(xmlDoc, udfNode, "fieldValue", value);
            AppendElement(xmlDoc, udfNode, "fieldType", type);
        }

        private void BuildTradeFees(XmlDocument xmlDoc, XmlNode tradeHeaderNode, string portfolio, decimal margin,
            string tradedate, string reportingEntity)
        {
            var tradeFeesNode = xmlDoc.CreateElement("tradeFees");
            tradeHeaderNode.AppendChild(tradeFeesNode);

            var tradeFeeNode = xmlDoc.CreateElement("tradeFee");
            SetAttribute(xmlDoc, tradeFeeNode, "index", "0");
            tradeFeesNode.AppendChild(tradeFeeNode);

            // Payer
            var payerPartyRef = xmlDoc.CreateElement("payerPartyReference");
            SetAttribute(xmlDoc, payerPartyRef, "href", "#SWEDBANK");
            tradeFeeNode.AppendChild(payerPartyRef);

            var payerPortfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, payerPortfolioRef, "href", "#" + portfolio);
            payerPartyRef.AppendChild(payerPortfolioRef);

            // Receiver
            var receiverPartyRef = xmlDoc.CreateElement("receiverPartyReference");
            SetAttribute(xmlDoc, receiverPartyRef, "href", "#SWEDBANK");
            tradeFeeNode.AppendChild(receiverPartyRef);

            var marginPortfolio = DetermineMarginPortfolio(reportingEntity);
            var receiverPortfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, receiverPortfolioRef, "href", "#" + marginPortfolio);
            receiverPartyRef.AppendChild(receiverPortfolioRef);

            // Fee details
            AppendElement(xmlDoc, tradeFeeNode, "feeType", "internalFee");
            AppendElement(xmlDoc, tradeFeeNode, "feeCurrency", "SEK");
            AppendElement(xmlDoc, tradeFeeNode, "feeAmount", margin.ToString(CultureInfo.InvariantCulture));
            AppendElement(xmlDoc, tradeFeeNode, "feeDateType", "transactionDate");
            AppendElement(xmlDoc, tradeFeeNode, "feeDate", tradedate);
            AppendElement(xmlDoc, tradeFeeNode, "autoShell", "false");
            AppendElement(xmlDoc, tradeFeeNode, "autoFee", "false");
            AppendElement(xmlDoc, tradeFeeNode, "feeRate", "0");
        }

        private void BuildTradeBody(XmlDocument xmlDoc, XmlNode tradeNode, string buySell, string callPut,
            string portfolio, string counterpartId, string expirydate, string cut, string settlementdate,
            string ccy1, string ccy2, decimal notional1, decimal notional2, decimal strike,
            string premiumdate, string premiumCurrency, decimal premium)
        {
            var tradeBodyNode = xmlDoc.CreateElement("tradeBody");
            tradeNode.AppendChild(tradeBodyNode);

            var fxOptionNode = xmlDoc.CreateElement("fxOption");
            tradeBodyNode.AppendChild(fxOptionNode);

            var fxVanillaOptionNode = xmlDoc.CreateElement("fxVanillaOption");
            fxOptionNode.AppendChild(fxVanillaOptionNode);

            var optionNode = xmlDoc.CreateElement("option");
            fxVanillaOptionNode.AppendChild(optionNode);

            AppendElement(xmlDoc, optionNode, "optionStyle", "european");

            // Buyer/Seller
            var holderRef = xmlDoc.CreateElement("optionHolderReference");
            SetAttribute(xmlDoc, holderRef, "href", buySell == "Buy" ? "#SWEDBANK" : "#_" + counterpartId);
            optionNode.AppendChild(holderRef);

            if (buySell == "Buy")
            {
                var portfolioRef = xmlDoc.CreateElement("portfolioReference");
                SetAttribute(xmlDoc, portfolioRef, "href", "#" + portfolio);
                holderRef.AppendChild(portfolioRef);
            }

            var writerRef = xmlDoc.CreateElement("optionWriterReference");
            SetAttribute(xmlDoc, writerRef, "href", buySell == "Sell" ? "#SWEDBANK" : "#_" + counterpartId);
            optionNode.AppendChild(writerRef);

            if (buySell == "Sell")
            {
                var portfolioRef = xmlDoc.CreateElement("portfolioReference");
                SetAttribute(xmlDoc, portfolioRef, "href", "#" + portfolio);
                writerRef.AppendChild(portfolioRef);
            }

            // Expiry and cut
            var optionMaturityNode = xmlDoc.CreateElement("optionMaturity");
            optionNode.AppendChild(optionMaturityNode);
            AppendElement(xmlDoc, optionMaturityNode, "date", expirydate);
            AppendElement(xmlDoc, optionMaturityNode, "cutOff", cut);

            AppendElement(xmlDoc, optionNode, "optionExercizeMethod", "delivery");

            // Call leg
            var callCurrencyAmountNode = xmlDoc.CreateElement("callCurrencyAmount");
            fxVanillaOptionNode.AppendChild(callCurrencyAmountNode);
            AppendElement(xmlDoc, callCurrencyAmountNode, "date", settlementdate);
            AppendElement(xmlDoc, callCurrencyAmountNode, "currency", callPut == "Call" ? ccy1 : ccy2);
            AppendElement(xmlDoc, callCurrencyAmountNode, "amount", (callPut == "Call" ? notional1 : notional2).ToString(CultureInfo.InvariantCulture));

            // Put leg
            var putCurrencyAmountNode = xmlDoc.CreateElement("putCurrencyAmount");
            fxVanillaOptionNode.AppendChild(putCurrencyAmountNode);
            AppendElement(xmlDoc, putCurrencyAmountNode, "date", settlementdate);
            AppendElement(xmlDoc, putCurrencyAmountNode, "currency", callPut == "Put" ? ccy1 : ccy2);
            AppendElement(xmlDoc, putCurrencyAmountNode, "amount", (callPut == "Put" ? notional1 : notional2).ToString(CultureInfo.InvariantCulture));

            // Strike
            var fxStrikeNode = xmlDoc.CreateElement("fxStrike");
            fxVanillaOptionNode.AppendChild(fxStrikeNode);
            var exchangeRateNode = xmlDoc.CreateElement("exchangeRate");
            fxStrikeNode.AppendChild(exchangeRateNode);
            var fxQuotationNode = xmlDoc.CreateElement("fxQuotation");
            exchangeRateNode.AppendChild(fxQuotationNode);

            AppendElement(xmlDoc, fxQuotationNode, "currency1", ccy2);
            AppendElement(xmlDoc, fxQuotationNode, "currency2", ccy1);
            AppendElement(xmlDoc, fxQuotationNode, "fxQuoteBasis", "currency1PerCurrency2");
            AppendElement(xmlDoc, fxQuotationNode, "formFactor", "1");

            AppendElement(xmlDoc, exchangeRateNode, "rate", strike.ToString(CultureInfo.InvariantCulture));

            // Premium
            BuildPremiumSettlement(xmlDoc, fxOptionNode, buySell, portfolio, counterpartId, premiumdate,
                premiumCurrency, premium, ccy1, ccy2);
        }

        private void BuildPremiumSettlement(XmlDocument xmlDoc, XmlNode fxOptionNode, string buySell,
            string portfolio, string counterpartId, string premiumdate, string premiumCurrency, decimal premium,
            string ccy1, string ccy2)
        {
            var settlementNode = xmlDoc.CreateElement("settlement");
            fxOptionNode.AppendChild(settlementNode);

            var settlementFlowNode = xmlDoc.CreateElement("settlementFlow");
            settlementNode.AppendChild(settlementFlowNode);

            var flowNode = xmlDoc.CreateElement("flow");
            settlementFlowNode.AppendChild(flowNode);

            // Payer/Receiver
            var payerRef = xmlDoc.CreateElement("payerPartyReference");
            SetAttribute(xmlDoc, payerRef, "href", buySell == "Buy" ? "#SWEDBANK" : "#_" + counterpartId);
            flowNode.AppendChild(payerRef);

            if (buySell == "Buy")
            {
                var portfolioRef = xmlDoc.CreateElement("portfolioReference");
                SetAttribute(xmlDoc, portfolioRef, "href", "#" + portfolio);
                payerRef.AppendChild(portfolioRef);
            }

            var receiverRef = xmlDoc.CreateElement("receiverPartyReference");
            SetAttribute(xmlDoc, receiverRef, "href", buySell == "Sell" ? "#SWEDBANK" : "#_" + counterpartId);
            flowNode.AppendChild(receiverRef);

            if (buySell == "Sell")
            {
                var portfolioRef = xmlDoc.CreateElement("portfolioReference");
                SetAttribute(xmlDoc, portfolioRef, "href", "#" + portfolio);
                receiverRef.AppendChild(portfolioRef);
            }

            AppendElement(xmlDoc, flowNode, "date", premiumdate);
            AppendElement(xmlDoc, flowNode, "currency", premiumCurrency);
            AppendElement(xmlDoc, flowNode, "amount", premium.ToString(CultureInfo.InvariantCulture));

            // Premium quotation
            var priceNode = xmlDoc.CreateElement("price");
            settlementFlowNode.AppendChild(priceNode);

            var priceExpressionNode = xmlDoc.CreateElement("priceExpression");
            priceNode.AppendChild(priceExpressionNode);

            var fxPremiumQuotationNode = xmlDoc.CreateElement("fxPremiumQuotation");
            priceExpressionNode.AppendChild(fxPremiumQuotationNode);

            if (premiumCurrency == ccy2)
            {
                AppendElement(xmlDoc, fxPremiumQuotationNode, "currency1", ccy2);
                AppendElement(xmlDoc, fxPremiumQuotationNode, "currency2", ccy1);
            }
            else
            {
                AppendElement(xmlDoc, fxPremiumQuotationNode, "currency1", ccy1);
                AppendElement(xmlDoc, fxPremiumQuotationNode, "currency2", ccy2);
            }

            AppendElement(xmlDoc, fxPremiumQuotationNode, "fxQuoteBasis", "currency1PerCurrency2");
            AppendElement(xmlDoc, fxPremiumQuotationNode, "formFactor", "1000");
        }

        private void BuildTradeInputConditions(XmlDocument xmlDoc, XmlNode tradeNode, string trader, string tradedate)
        {
            var tradeInputConditionsNode = xmlDoc.CreateElement("tradeInputConditions");
            tradeNode.AppendChild(tradeInputConditionsNode);

            var inputByNode = xmlDoc.CreateElement("inputBy");
            tradeInputConditionsNode.AppendChild(inputByNode);

            AppendElement(xmlDoc, inputByNode, "userName", trader);
            AppendElement(xmlDoc, tradeInputConditionsNode, "systemDate", tradedate);
        }

        private string DetermineMarginPortfolio(string reportingEntity)
        {
            if (string.IsNullOrEmpty(reportingEntity))
                return "FX_INST";

            switch (reportingEntity)
            {
                case "CORPORATE SALES FINLAND":
                    return "FX_HELSINKI";
                case "CORPORATE SALES GOTHENBURG":
                case "CORPORATE SALES MALMO":
                case "CORPORATE SALES STOCKHOLM":
                    return "FX_RETAIL";
                case "CORPORATE SALES NORWAY":
                    return "FXOPT_NORWAY";
                case "FX INSTITUTIONAL CLIENTS":
                    return "FX_INST";
                case "FX VOLATILITY":
                    return "FX_INST";
                default:
                    return "FX_INST";
            }
        }

        private void AppendElement(XmlDocument xmlDoc, XmlNode parentNode, string elementName, string innerText)
        {
            var element = xmlDoc.CreateElement(elementName);
            element.InnerText = innerText;
            parentNode.AppendChild(element);
        }

        private void SetAttribute(XmlDocument xmlDoc, XmlNode node, string attributeName, string value)
        {
            var attribute = xmlDoc.CreateAttribute(attributeName);
            attribute.Value = value;
            node.Attributes.Append(attribute);
        }

        #endregion
    }
}
