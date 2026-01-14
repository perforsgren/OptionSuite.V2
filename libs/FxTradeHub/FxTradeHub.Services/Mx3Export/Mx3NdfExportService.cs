using System;
using System.Globalization;
using System.IO;
using System.Xml;
using FxTradeHub.Contracts.Dtos;
using FxSharedConfig;

namespace FxTradeHub.Services.Mx3Export
{
    /// <summary>
    /// Service för att exportera NDF trades till MX3 XML-format.
    /// </summary>
    public sealed class Mx3NdfExportService
    {
        public Mx3OptionExportResult CreateXmlFile(Mx3NdfExportRequest request)
        {
            try
            {
                var exportFolder = AppPaths.Mx3ImportFolder;
                var fileName = $"{request.StpTradeId}_{request.TradeId}.xml";
                var fullPath = Path.Combine(exportFolder, fileName);

                // ✅ DEBUG: Logga margin-info
                System.Diagnostics.Debug.WriteLine($"[Mx3NdfExport] StpTradeId={request.StpTradeId}, Margin={request.Margin}, ReportingEntity={request.ReportingEntityId}");


                var xmlDoc = BuildXmlDocument(request);
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

        private XmlDocument BuildXmlDocument(Mx3NdfExportRequest req)
        {
            var xmlDoc = new XmlDocument();

            // Validering
            if (string.IsNullOrEmpty(req.CurrencyPair) || req.CurrencyPair.Length != 6)
                throw new ArgumentException($"Invalid CurrencyPair: {req.CurrencyPair}");

            if (string.IsNullOrEmpty(req.SettlementCurrency) || req.SettlementCurrency.Length != 3)
                throw new ArgumentException($"Invalid SettlementCurrency: {req.SettlementCurrency}");

            if (string.IsNullOrEmpty(req.NotionalCurrency) || req.NotionalCurrency.Length != 3)
                throw new ArgumentException($"Invalid NotionalCurrency: {req.NotionalCurrency}");

            // Parse currency pair
            var baseCcy = req.CurrencyPair.Substring(0, 3);   // USD
            var quoteCcy = req.CurrencyPair.Substring(3, 3);  // CNY

            // Beräkna flows baserat på NotionalCurrency och BuySell
            decimal notional1;
            decimal notional2;
            string ccy1;
            string ccy2;

            if (req.BuySell == "Buy")
            {
                // Buy base currency (USD i USDCNY)
                if (req.NotionalCurrency == quoteCcy)
                {
                    // Notional är i quote currency (CNY): vi betalar CNY, får USD
                    ccy1 = quoteCcy;  // Vi betalar (CNY)
                    ccy2 = baseCcy;   // Vi får (USD)
                    notional1 = req.Notional;              // CNY
                    notional2 = req.Notional / req.Rate;   // USD = CNY / Rate
                }
                else if (req.NotionalCurrency == baseCcy)
                {
                    // Notional är i base currency (USD): vi betalar quote, får base
                    ccy1 = quoteCcy;  // Vi betalar (CNY)
                    ccy2 = baseCcy;   // Vi får (USD)
                    notional1 = req.Notional * req.Rate;   // CNY = USD * Rate
                    notional2 = req.Notional;              // USD
                }
                else
                {
                    throw new ArgumentException(
                        $"NotionalCurrency '{req.NotionalCurrency}' does not match CurrencyPair '{req.CurrencyPair}'. " +
                        $"Expected either '{baseCcy}' or '{quoteCcy}'.");
                }
            }
            else if (req.BuySell == "Sell")
            {
                // Sell base currency (USD i USDCNY) = vi säljer USD, får CNY
                if (req.NotionalCurrency == quoteCcy)
                {
                    // Notional är i quote currency (CNY): vi får CNY notional, betalar USD
                    ccy1 = baseCcy;   // Vi betalar (USD)
                    ccy2 = quoteCcy;  // Vi får (CNY)
                    notional1 = req.Notional / req.Rate;   // USD = CNY / Rate
                    notional2 = req.Notional;              // CNY
                }
                else if (req.NotionalCurrency == baseCcy)
                {
                    // Notional är i base currency (USD): vi betalar USD notional, får CNY
                    ccy1 = baseCcy;   // Vi betalar (USD)
                    ccy2 = quoteCcy;  // Vi får (CNY)
                    notional1 = req.Notional;              // USD
                    notional2 = req.Notional * req.Rate;   // CNY = USD * Rate
                }
                else
                {
                    throw new ArgumentException(
                        $"NotionalCurrency '{req.NotionalCurrency}' does not match CurrencyPair '{req.CurrencyPair}'. " +
                        $"Expected either '{baseCcy}' or '{quoteCcy}'.");
                }
            }
            else
            {
                throw new ArgumentException($"Invalid BuySell value: '{req.BuySell}'. Expected 'Buy' or 'Sell'.");
            }

            // Format dates
            var tradedate = req.TradeDate.ToString("yyyyMMdd");
            var settlementdate = req.SettlementDate.ToString("yyyyMMdd");
            var fixingdate = req.FixingDate.ToString("yyyyMMdd");

            #region Root: MxML
            var rootNode = xmlDoc.CreateElement("MxML");
            var versionAttr = xmlDoc.CreateAttribute("version");
            versionAttr.Value = "1-1";
            rootNode.Attributes.Append(versionAttr);
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
            AppendElement(xmlDoc, inputByNode, "userName", req.Trader);
            AppendElement(xmlDoc, inputByNode, "group", "FO");
            AppendElement(xmlDoc, inputByNode, "desk", "STKFXD");
            #endregion

            #region contracts
            var contractsNode = xmlDoc.CreateElement("contracts");
            rootNode.AppendChild(contractsNode);

            var contractNode = xmlDoc.CreateElement("contract");
            SetAttribute(xmlDoc, contractNode, "id", "contract_0");
            SetAttribute(xmlDoc, contractNode, "mefClass", "mxContractISINGLE");
            SetAttribute(xmlDoc, contractNode, "mefClassInstanceLabel", "Spot-Forward"); 
            contractsNode.AppendChild(contractNode);

            var contractHeaderNode = xmlDoc.CreateElement("contractHeader");
            contractNode.AppendChild(contractHeaderNode);

            var contractCategoryNode = xmlDoc.CreateElement("contractCategory");
            contractHeaderNode.AppendChild(contractCategoryNode);
            AppendElement(xmlDoc, contractCategoryNode, "typology", "FXD: Non Deliv Fwd");

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

            #region trades
            var tradesNode = xmlDoc.CreateElement("trades");
            rootNode.AppendChild(tradesNode);

            var tradeNode = xmlDoc.CreateElement("trade");
            SetAttribute(xmlDoc, tradeNode, "id", "trade_0");
            SetAttribute(xmlDoc, tradeNode, "mefClass", "mxContractITRADE");
            tradesNode.AppendChild(tradeNode);

            BuildTradeParties(xmlDoc, tradeNode, req.Counterparty);
            BuildTradePortfolios(xmlDoc, tradeNode, req.Portfolio, req.Margin, req.ReportingEntityId);
            BuildTradeHeader(xmlDoc, tradeNode, tradedate, req.Portfolio, req.Trader, req.TradeId, req);
            BuildTradeBody(xmlDoc, tradeNode, req.Portfolio, ccy1, ccy2, notional1, notional2,
                settlementdate, fixingdate, req.Rate, req.CurrencyPair, req.SettlementCurrency, req.FixingSource, req.Counterparty);
            BuildTradeInputConditions(xmlDoc, tradeNode, req.Trader, tradedate);
            #endregion

            return xmlDoc;
        }

        #region Helper methods

        private void BuildTradeParties(XmlDocument xmlDoc, XmlNode tradeNode, string counterparty)
        {
            var partiesNode = xmlDoc.CreateElement("parties");
            tradeNode.AppendChild(partiesNode);

            var party1 = xmlDoc.CreateElement("party");
            SetAttribute(xmlDoc, party1, "id", "SWEDBANK");
            partiesNode.AppendChild(party1);
            AppendElement(xmlDoc, party1, "partyName", "SWEDBANK");

            var party2 = xmlDoc.CreateElement("party");
            SetAttribute(xmlDoc, party2, "id", "_24");
            partiesNode.AppendChild(party2);
            AppendElement(xmlDoc, party2, "partyName", counterparty ?? "COUNTERPARTY");  // ✅ Verkligt namn
        }

        private void BuildTradePortfolios(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio, decimal? margin, string reportingEntityId)
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
                var marginPortfolio = DetermineMarginPortfolio(reportingEntityId);
                var portfolioNodeMargin = xmlDoc.CreateElement("portfolio");
                SetAttribute(xmlDoc, portfolioNodeMargin, "id", marginPortfolio);
                portfoliosNode.AppendChild(portfolioNodeMargin);
                AppendElement(xmlDoc, portfolioNodeMargin, "portfolioLabel", marginPortfolio);
            }
        }

        private void BuildTradeHeader(XmlDocument xmlDoc, XmlNode tradeNode, string tradedate,
            string portfolio, string trader, string tradeId, Mx3NdfExportRequest req)
        {
            var tradeHeaderNode = xmlDoc.CreateElement("tradeHeader");
            tradeNode.AppendChild(tradeHeaderNode);

            AppendElement(xmlDoc, tradeHeaderNode, "tradeDate", tradedate);

            // tradeCategory
            var tradeCategoryNode = xmlDoc.CreateElement("tradeCategory");
            tradeHeaderNode.AppendChild(tradeCategoryNode);
            AppendElement(xmlDoc, tradeCategoryNode, "tradeDestination", "external");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeFamily", "CURR");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeGroup", "FXD");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeType", "FXD");
            AppendElement(xmlDoc, tradeCategoryNode, "typology", "FXD: Non Deliv Fwd");
            AppendElement(xmlDoc, tradeCategoryNode, "mainTypologyPath", "/Typologies/ALL/FXD: Non Deliv Fwd");

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

            // View 2: Counterparty
            var tradeView2 = xmlDoc.CreateElement("tradeView");
            tradeViewsNode.AppendChild(tradeView2);

            var partyRef2 = xmlDoc.CreateElement("partyReference");
            SetAttribute(xmlDoc, partyRef2, "href", "#_24");
            tradeView2.AppendChild(partyRef2);

            // ✅ MiFID User Defined Fields
            var udfNode = xmlDoc.CreateElement("tradeUserDefinedFields");
            tradeHeaderNode.AppendChild(udfNode);

            // FO_COMMENT (TradeId)
            var foCommentNode = xmlDoc.CreateElement("userDefinedField");
            udfNode.AppendChild(foCommentNode);
            AppendElement(xmlDoc, foCommentNode, "fieldLabel", "FO_COMMENT");
            AppendElement(xmlDoc, foCommentNode, "fieldValue", tradeId);
            AppendElement(xmlDoc, foCommentNode, "fieldType", "character");

            // SALES (margin)
            if (req.Margin.HasValue && req.Margin.Value != 0)
            {
                var salesNode = xmlDoc.CreateElement("userDefinedField");
                udfNode.AppendChild(salesNode);
                AppendElement(xmlDoc, salesNode, "fieldLabel", "SALES");
                AppendElement(xmlDoc, salesNode, "fieldValue", req.Margin.Value.ToString(CultureInfo.InvariantCulture));
                AppendElement(xmlDoc, salesNode, "fieldType", "numeric");
            }

            // MIC
            if (!string.IsNullOrEmpty(req.Mic))
            {
                var micNode = xmlDoc.CreateElement("userDefinedField");
                udfNode.AppendChild(micNode);
                AppendElement(xmlDoc, micNode, "fieldLabel", "MIFID_MIC");
                AppendElement(xmlDoc, micNode, "fieldValue", req.Mic);
                AppendElement(xmlDoc, micNode, "fieldType", "character");
            }

            // ISIN
            if (!string.IsNullOrEmpty(req.Isin))
            {
                var isinNode = xmlDoc.CreateElement("userDefinedField");
                udfNode.AppendChild(isinNode);
                AppendElement(xmlDoc, isinNode, "fieldLabel", "MIFID_ISIN");
                AppendElement(xmlDoc, isinNode, "fieldValue", req.Isin);
                AppendElement(xmlDoc, isinNode, "fieldType", "character");
            }

            // InvId
            if (!string.IsNullOrEmpty(req.InvId))
            {
                var invIdNode = xmlDoc.CreateElement("userDefinedField");
                udfNode.AppendChild(invIdNode);
                AppendElement(xmlDoc, invIdNode, "fieldLabel", "INV_DEC_ID");
                AppendElement(xmlDoc, invIdNode, "fieldValue", req.InvId);
                AppendElement(xmlDoc, invIdNode, "fieldType", "character");
            }

            // ReportingEntityId
            if (!string.IsNullOrEmpty(req.ReportingEntityId))
            {
                var reportingNode = xmlDoc.CreateElement("userDefinedField");
                udfNode.AppendChild(reportingNode);
                AppendElement(xmlDoc, reportingNode, "fieldLabel", "REPORT_ENT");
                AppendElement(xmlDoc, reportingNode, "fieldValue", req.ReportingEntityId);
                AppendElement(xmlDoc, reportingNode, "fieldType", "character");
            }

            // ExecutionTime
            if (req.ExecutionTimeUtc.HasValue)
            {
                var execTimeNode = xmlDoc.CreateElement("userDefinedField");
                udfNode.AppendChild(execTimeNode);
                AppendElement(xmlDoc, execTimeNode, "fieldLabel", "EXEC_TIME");
                AppendElement(xmlDoc, execTimeNode, "fieldValue", req.ExecutionTimeUtc.Value.ToString("yyyyMMdd HH:mm:ss.fff"));
                AppendElement(xmlDoc, execTimeNode, "fieldType", "character");
            }

            // ✅ Trade fees (margin)
            if (req.Margin.HasValue && req.Margin.Value != 0)
            {
                BuildTradeFees(xmlDoc, tradeHeaderNode, portfolio, req.Margin.Value, tradedate, req.ReportingEntityId);
            }
        }

        private void BuildTradeBody(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio,
            string ccy1, string ccy2, decimal notional1, decimal notional2,
            string settlementdate, string fixingdate, decimal rate, string ccyPair,
            string settlementCurrency, string fixingSource, string counterparty)
        {
            var tradeBodyNode = xmlDoc.CreateElement("tradeBody");
            tradeNode.AppendChild(tradeBodyNode);

            // ✅ fxSpotForward är wrapper
            var fxSpotForwardNode = xmlDoc.CreateElement("fxSpotForward");
            tradeBodyNode.AppendChild(fxSpotForwardNode);

            // currency1Flow (we PAY)
            var currency1FlowNode = xmlDoc.CreateElement("currency1Flow");
            fxSpotForwardNode.AppendChild(currency1FlowNode);

            var payerPartyRef1 = xmlDoc.CreateElement("payerPartyReference");
            SetAttribute(xmlDoc, payerPartyRef1, "href", "#SWEDBANK");
            currency1FlowNode.AppendChild(payerPartyRef1);

            var payerPortfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, payerPortfolioRef, "href", "#" + portfolio);
            payerPartyRef1.AppendChild(payerPortfolioRef);

            var receiverPartyRef1 = xmlDoc.CreateElement("receiverPartyReference");
            SetAttribute(xmlDoc, receiverPartyRef1, "href", "#_24");
            currency1FlowNode.AppendChild(receiverPartyRef1);

            AppendElement(xmlDoc, currency1FlowNode, "date", settlementdate);
            AppendElement(xmlDoc, currency1FlowNode, "currency", ccy1);
            AppendElement(xmlDoc, currency1FlowNode, "amount", notional1.ToString(CultureInfo.InvariantCulture));

            // currency2Flow (we RECEIVE)
            var currency2FlowNode = xmlDoc.CreateElement("currency2Flow");
            fxSpotForwardNode.AppendChild(currency2FlowNode);

            var payerPartyRef2 = xmlDoc.CreateElement("payerPartyReference");
            SetAttribute(xmlDoc, payerPartyRef2, "href", "#_24");
            currency2FlowNode.AppendChild(payerPartyRef2);

            var receiverPartyRef2 = xmlDoc.CreateElement("receiverPartyReference");
            SetAttribute(xmlDoc, receiverPartyRef2, "href", "#SWEDBANK");
            currency2FlowNode.AppendChild(receiverPartyRef2);

            var receiverPortfolioRef = xmlDoc.CreateElement("portfolioReference");
            SetAttribute(xmlDoc, receiverPortfolioRef, "href", "#" + portfolio);
            receiverPartyRef2.AppendChild(receiverPortfolioRef);

            AppendElement(xmlDoc, currency2FlowNode, "date", settlementdate);
            AppendElement(xmlDoc, currency2FlowNode, "currency", ccy2);
            AppendElement(xmlDoc, currency2FlowNode, "amount", notional2.ToString(CultureInfo.InvariantCulture));

            // exchangeRate
            var exchangeRateNode = xmlDoc.CreateElement("exchangeRate");
            fxSpotForwardNode.AppendChild(exchangeRateNode);

            var fxQuotationNode = xmlDoc.CreateElement("fxQuotation");
            exchangeRateNode.AppendChild(fxQuotationNode);

            AppendElement(xmlDoc, fxQuotationNode, "currency1", ccyPair.Substring(3, 3));
            AppendElement(xmlDoc, fxQuotationNode, "currency2", ccyPair.Substring(0, 3));
            AppendElement(xmlDoc, fxQuotationNode, "fxQuoteBasis", "currency1PerCurrency2");
            AppendElement(xmlDoc, fxQuotationNode, "formFactor", "1");

            AppendElement(xmlDoc, exchangeRateNode, "rate", rate.ToString(CultureInfo.InvariantCulture));

            // ✅ nonDeliverableForward som UNDERNODE till fxSpotForward
            var ndfNode = xmlDoc.CreateElement("nonDeliverableForward");
            fxSpotForwardNode.AppendChild(ndfNode);

            AppendElement(xmlDoc, ndfNode, "settlementCurrency", settlementCurrency);

            var fxFixingNode = xmlDoc.CreateElement("fxFixing");
            ndfNode.AppendChild(fxFixingNode);

            AppendElement(xmlDoc, fxFixingNode, "currency1", ccyPair.Substring(0, 3));
            AppendElement(xmlDoc, fxFixingNode, "currency2", ccyPair.Substring(3, 3));

            var fxRateSourceNode = xmlDoc.CreateElement("fxRateSource");
            fxFixingNode.AppendChild(fxRateSourceNode);
            AppendElement(xmlDoc, fxRateSourceNode, "sourceLabel", fixingSource ?? "NDF_group");
            AppendElement(xmlDoc, fxRateSourceNode, "columnLabel", "Fixing");

            AppendElement(xmlDoc, fxFixingNode, "date", fixingdate);

            AppendElement(xmlDoc, ndfNode, "ndfEstimationDate", "fixingValueDate");

            // ✅ Obligatoriska fält för fxSpotForward
            AppendElement(xmlDoc, fxSpotForwardNode, "fxAsianAverageRounding", "Disabled");
            AppendElement(xmlDoc, fxSpotForwardNode, "forwardDelivery", "forward");
            AppendElement(xmlDoc, fxSpotForwardNode, "fxQuantitiesInputMode", "oneQuantity");
            AppendElement(xmlDoc, fxSpotForwardNode, "riskSection", $"{ccyPair.Substring(0, 3)}/{ccyPair.Substring(3, 3)}"); 
            AppendElement(xmlDoc, fxSpotForwardNode, "offsettingBuySellContribution", "false");
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

        private void BuildTradeFees(XmlDocument xmlDoc, XmlNode tradeHeaderNode, string portfolio, decimal margin,
            string tradedate, string reportingEntityId)
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

            var marginPortfolio = DetermineMarginPortfolio(reportingEntityId);
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
    }
}
