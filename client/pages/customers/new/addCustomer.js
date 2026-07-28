const apiRoot = BASE_URL + '/api/customers';

$(document).ready(function () {
    $('#addCustomerForm').submit(function (e) {
        e.preventDefault();
        setLoading(true);

        const body = {
            customerName: $('#customerName').val(),
            email: $('#email').val(),
            phone: $('#phone').val(),
            address: $('#address').val()
        };

        ajaxCall("POST", apiRoot, JSON.stringify(body), addCustomerSCB, addCustomerECB);
    });
});

function setLoading(on) {
    $('#saveBtn').toggle(!on);
    $('#loader').toggleClass('active', on);
}

function addCustomerSCB() {
    window.location.href = '/pages/customers';
}

function addCustomerECB(xhr, status, err) {
    console.log('Error:', status, err);
    alert('שגיאה בשמירה, נסה שוב');
    setLoading(false);
}
