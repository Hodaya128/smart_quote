// settings.js

var allSettings = [];
const apiRoot = BASE_URL + "/api/settings";

$(document).ready(function () {
  $("#preferredCurrency").val(localStorage.getItem('preferredCurrency') || 'USD');
  getSettings();
  $("#settingsForm").submit(function (e) {
    e.preventDefault();
    saveSettings();
  });

  // function getSettings () {
  // ajaxCall("GET", apiRoot, "", getSettingsSCB, getSettingsECB);
  // }
  function getSettings() {
    console.log("שולח בקשה ל:", apiRoot); // ← הוסיפי את השורה הזו
    ajaxCall("GET", apiRoot, "", getSettingsSCB, getSettingsECB);
  }

  function getSettingsSCB(data) {
    allSettings = data;
    renderSettings(data);
  }

  function getSettingsECB(xhr, status, error) {
    console.log("Error loading JSON:", status, error);
    alert("בעיה בטעינה, נסה שוב מאוחר יותר");
  }

  //   function renderSettings(data) {
  //     $("#minProfit").val(data.MinProfitPercent);
  //     $("#minSavingAlt").val(data.MinSavingPercent);
  //     $("#minSavingSupply").val(data.MinSavingPercentConfig);
  //     $("#maxQtyIncrease").val(data.MaxIncreasePercent);
  //     $("#minSavingQty").val(data.MinSavingPercentQuantity);
  //   }

  function renderSettings(data) {
    $("#minProfit").val(data.minProfitPercent);
    $("#minSavingAlt").val(data.minSavingPercent);
    $("#minSavingSupply").val(data.minSavingPercentConfig);
    $("#maxQtyIncrease").val(data.maxIncreasePercent);
    $("#minSavingQty").val(data.minSavingPercentQuantity);
    $("#preferredCurrency").val(data.preferredCurrency || "USD");
  }
  //   function saveSettings() {
  //     const body = {};
  //     body.MinProfitPercent = Number($("#minProfit").val());
  //     body.MinSavingPercent = Number($("#minSavingAlt").val());
  //     body.MinSavingPercentConfig = Number($("#minSavingSupply").val());
  //     body.MaxIncreasePercent = Number($("#maxQtyIncrease").val());
  //     body.MinSavingPercentQuantity = Number($("#minSavingQty").val());
  //     ajaxCall("POST", apiRoot, "", saveSettingsSCB, saveSettingsECB);
  //   }

  function saveSettings() {
    localStorage.setItem('preferredCurrency', $("#preferredCurrency").val());
    alert("נשמר בהצלחה");
  }
  function saveSettingsSCB(data) {
    alert("נשמר בהצלחה");
  }

  function saveSettingsECB(xhr, status, error) {
    console.log("Error loading JSON:", status, error);
    alert("בעיה בשמירה, נסה שוב מאוחר יותר");
  }
});
