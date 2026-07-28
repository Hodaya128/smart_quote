// function ajaxCall(method, api, data, success, error) {
//     $.ajax({
//         type: method,
//         url: api,
//         data: data,
//         cache: false,
//         contentType: "application/json",
//         dataType: "json",
//         success: success,
//         error: error
//     });
// }

function ajaxCall(method, api, data, success, error) {
  $.ajax({
    type: method,
    url: api,
    data: data ? JSON.stringify(data) : null, // ✅ ממיר ל־JSON
    cache: false,
    contentType: "application/json",
    dataType: "json",
    success: success,
    error: error,
  });
}
