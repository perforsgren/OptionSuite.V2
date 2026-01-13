// libs/FxTradeHub/FxTradeHub.Services/Mx3Export/Mx3LinearExportService.cs

using System;
using System.Globalization;
using System.IO;
using System.Xml;
using FxTradeHub.Contracts.Dtos;
using FxSharedConfig;

namespace FxTradeHub.Services.Mx3Export
{
    /// <summary>
    /// Service för att exportera linear trades (Spot/Forward) till MX3 XML-format.
    /// Följer samma mönster som Mx3OptionExportService.
    /// </summary>
    public sealed class Mx3LinearExportService
    {
        public Mx3OptionExportResult CreateXmlFile(Mx3LinearExportRequest request)
        {
            try
            {
                var exportFolder = AppPaths.Mx3ImportFolder;
                var fileName = $"{request.StpTradeId}_{request.TradeId}.xml";
                var fullPath = Path.Combine(exportFolder, fileName);

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

        private XmlDocument BuildXmlDocument(Mx3LinearExportRequest req)
        {
            var xmlDoc = new XmlDocument();

            // Parse currency pair
            var ccy1 = req.CurrencyPair.Substring(0, 3);
            var ccy2 = req.CurrencyPair.Substring(3, 3);

            // Format dates
            var tradedate = req.TradeDate.ToString("yyyyMMdd");
            var settlementdate = req.SettlementDate.ToString("yyyyMMdd");

            // Calculate notionals
            decimal notional1 = req.Notional;
            decimal notional2 = req.Notional * req.Rate;

            // Swap currencies if Buy (we receive ccy1, pay ccy2)
            if (req.BuySell == "Buy")
            {
                var tempCcy = ccy1;
                ccy1 = ccy2;
                ccy2 = tempCcy;

                var tempNotional = notional1;
                notional1 = notional2;
                notional2 = tempNotional;
            }

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
            AppendElement(xmlDoc, contractCategoryNode, "typology", "FX: Spot Forward");

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

            BuildTradeParties(xmlDoc, tradeNode, req.CounterpartId, req.Counterpart);
            BuildTradePortfolios(xmlDoc, tradeNode, req.Portfolio);
            BuildTradeHeader(xmlDoc, tradeNode, tradedate, req.Portfolio, req.Trader,
                req.CounterpartId, req.TradeId);
            BuildTradeBody(xmlDoc, tradeNode, req.Portfolio, req.CounterpartId,
                ccy1, ccy2, notional1, notional2, settlementdate, req.Rate, req.CurrencyPair);
            BuildTradeInputConditions(xmlDoc, tradeNode, req.Trader, tradedate);
            #endregion

            return xmlDoc;
        }

        #region Helper methods

        private void BuildTradeParties(XmlDocument xmlDoc, XmlNode tradeNode, string counterpartId, string counterpart)
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
            AppendElement(xmlDoc, party2, "partyName", "HEDGE");
        }

        private void BuildTradePortfolios(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio)
        {
            var portfoliosNode = xmlDoc.CreateElement("portfolios");
            tradeNode.AppendChild(portfoliosNode);

            var portfolioNode = xmlDoc.CreateElement("portfolio");
            SetAttribute(xmlDoc, portfolioNode, "id", portfolio);
            portfoliosNode.AppendChild(portfolioNode);
            AppendElement(xmlDoc, portfolioNode, "portfolioLabel", portfolio);
        }

        private void BuildTradeHeader(XmlDocument xmlDoc, XmlNode tradeNode, string tradedate,
            string portfolio, string trader, string counterpartId, string tradeId)
        {
            var tradeHeaderNode = xmlDoc.CreateElement("tradeHeader");
            tradeNode.AppendChild(tradeHeaderNode);

            AppendElement(xmlDoc, tradeHeaderNode, "tradeDate", tradedate);

            var tradeCategoryNode = xmlDoc.CreateElement("tradeCategory");
            tradeHeaderNode.AppendChild(tradeCategoryNode);
            AppendElement(xmlDoc, tradeCategoryNode, "tradeDestination", "external");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeFamily", "CURR");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeGroup", "FXD");
            AppendElement(xmlDoc, tradeCategoryNode, "tradeType", "FXD");

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

            // User defined fields
            var udfNode = xmlDoc.CreateElement("tradeUserDefinedFields");
            tradeHeaderNode.AppendChild(udfNode);

            var foCommentNode = xmlDoc.CreateElement("userDefinedField");
            udfNode.AppendChild(foCommentNode);
            AppendElement(xmlDoc, foCommentNode, "fieldLabel", "FO_COMMENT");
            AppendElement(xmlDoc, foCommentNode, "fieldValue", tradeId);
            AppendElement(xmlDoc, foCommentNode, "fieldType", "character");
        }

        private void BuildTradeBody(XmlDocument xmlDoc, XmlNode tradeNode, string portfolio,
            string counterpartId, string ccy1, string ccy2, decimal notional1, decimal notional2,
            string settlementdate, decimal rate, string ccyPair)
        {
            var tradeBodyNode = xmlDoc.CreateElement("tradeBody");
            tradeNode.AppendChild(tradeBodyNode);

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

            // fxSpot (same rate structure)
            var fxSpotNode = xmlDoc.CreateElement("fxSpot");
            fxSpotForwardNode.AppendChild(fxSpotNode);

            var fxQuotation2Node = xmlDoc.CreateElement("fxQuotation");
            fxSpotNode.AppendChild(fxQuotation2Node);

            AppendElement(xmlDoc, fxQuotation2Node, "currency1", ccyPair.Substring(3, 3));
            AppendElement(xmlDoc, fxQuotation2Node, "currency2", ccyPair.Substring(0, 3));
            AppendElement(xmlDoc, fxQuotation2Node, "fxQuoteBasis", "currency1PerCurrency2");
            AppendElement(xmlDoc, fxQuotation2Node, "formFactor", "1");

            AppendElement(xmlDoc, fxSpotNode, "rate", rate.ToString(CultureInfo.InvariantCulture));
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
    }
}
